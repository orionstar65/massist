using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace StealthInterviewAssistant.Services
{
    public class LiveCaptionsService
    {
        // ---------- Thread-safe state ----------
        private readonly object _stateLock = new object();

        // Rolling mirror (exact normalized system text right now)
        private string _mirror = string.Empty;
        private string _prevMirror = string.Empty;

        // Full transcript (never shrinks)
        private readonly StringBuilder _history = new StringBuilder(8192);
        private int _historySentPos = 0;      // index in history last sent to GPT
        private bool _baselineSnapped = false; // ensures initial backlog isn't sent

        // UIA
        private AutomationElement? _liveCaptionsWindow;
        private AutomationElement? _textContainer;
        private TextPattern? _textPattern;
        private AutomationEventHandler? _textChangedHandler;

        // Loop
        private System.Threading.Timer? _tick;
        private bool _isRunning = false;

        // Tunables
        private const int POLL_INTERVAL_MS   = 200;
        private const int MAX_MIRROR_CHARS   = 2_000_000; // safety cap for mirror
        private const int MAX_HISTORY_CHARS  = 20_000_000; // safety cap for full history (~20MB)

        // Overlap matching (robust to head trim & tail rewrites)
        private const int OVERLAP_PROBE = 400;
        private const int MIN_TRUSTED_OVERLAP = 24;

        // Scroll throttling
        private int _scrollTickMod = 0;

        // Logging
        private readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "livecaption.log");
        private readonly object _logLock = new object();

        // Public events
        public event Action<string>? OnNewText; // now emits FULL history text

        // ---------- Public API ----------

        public async Task<bool> StartAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    _liveCaptionsWindow = FindLiveCaptionsWindow();
                    if (_liveCaptionsWindow == null)
                    {
                        if (!TryLaunchLiveCaptions()) return false;
                        Thread.Sleep(2000);
                        for (int i = 0; i < 5 && _liveCaptionsWindow == null; i++)
                        {
                            _liveCaptionsWindow = FindLiveCaptionsWindow();
                            Thread.Sleep(1000);
                        }
                        if (_liveCaptionsWindow == null) return false;
                    }

                    _textContainer = FindTextContainer(_liveCaptionsWindow);
                    if (_textContainer == null) return false;

                    if (!_textContainer.TryGetCurrentPattern(TextPattern.Pattern, out var p)) return false;
                    _textPattern = p as TextPattern;
                    if (_textPattern == null) return false;

                    _textChangedHandler = OnTextChanged;
                    Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, _textContainer, TreeScope.Element, _textChangedHandler);

                    // Initial snapshot
                    string initialRaw = ReadSystemTextSafely();
                    string initial = NormalizeSystemText(initialRaw);

                    lock (_stateLock)
                    {
                        _mirror = initial;
                        _prevMirror = _mirror;

                        // Start history with what we see at boot
                        _history.Clear();
                        _history.Append(_mirror);

                        // Snap baseline so initial backlog isn't sent on first hotkey
                        _historySentPos = _history.Length;
                        _baselineSnapped = true;
                    }

                    // UI now shows FULL HISTORY
                    NotifyUI(GetHistoryUnsafe());

                    _isRunning = true;
                    _tick = new System.Threading.Timer(Tick, null, POLL_INTERVAL_MS, POLL_INTERVAL_MS);
                    TryMinimizeWindow(_liveCaptionsWindow);

                    Log("START", initial, $"mirror={_mirror.Length}, history={_history.Length}");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"StartAsync error: {ex.Message}");
                    return false;
                }
            });
        }

        public void Stop()
        {
            _isRunning = false;

            var t = _tick;
            _tick = null;
            t?.Dispose();

            if (_textContainer != null && _textChangedHandler != null)
            {
                try { Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent, _textContainer, _textChangedHandler); }
                catch { }
            }

            _textChangedHandler = null;
            _textPattern = null;
            _textContainer = null;
            _liveCaptionsWindow = null;
        }

        /// <summary>
        /// Exact text shown in your app. Now this returns the FULL transcript history.
        /// (Your UI uses OnNewText already; this keeps behavior consistent.)
        /// </summary>
        public string GetAll()
        {
            lock (_stateLock) return _history.ToString();
        }

        /// <summary>
        /// Returns ONLY the newly appended history since the last send.
        /// This is independent from the rolling system buffer.
        /// </summary>
        public string GetLastPart()
        {
            lock (_stateLock)
            {
                if (!_baselineSnapped)
                {
                    _historySentPos = _history.Length;
                    _baselineSnapped = true;
                    return string.Empty;
                }
                if (_historySentPos >= _history.Length) return string.Empty;
                return _history.ToString(_historySentPos, _history.Length - _historySentPos);
            }
        }

        /// <summary>
        /// Marks current history end as sent.
        /// </summary>
        public void MarkLastPartAsSent()
        {
            lock (_stateLock)
            {
                _historySentPos = _history.Length;
                _baselineSnapped = true;
                Debug.WriteLine($"History baseline advanced. sentPos={_historySentPos}, historyLen={_history.Length}");
            }
        }

        /// <summary>
        /// Clears history and mirror (rare; typically you won't call this during a session).
        /// </summary>
        public void Clear()
        {
            lock (_stateLock)
            {
                _mirror = string.Empty;
                _prevMirror = string.Empty;
                _history.Clear();
                _historySentPos = 0;
                _baselineSnapped = false;
            }
            NotifyUI(string.Empty);
            Log("CLEAR", "", "Buffers cleared");
        }

        // ---------- Core loop ----------

        private void Tick(object? _)
        {
            if (!_isRunning || _textPattern == null) return;

            TryOccasionalScroll();

            string systemRaw;
            try { systemRaw = _textPattern.DocumentRange.GetText(-1) ?? string.Empty; }
            catch { return; }

            string system = NormalizeSystemText(systemRaw);
            string? uiTextToRaise = null;

            lock (_stateLock)
            {
                if (system != _prevMirror)
                {
                    // Compute "net new" relative to previous mirror and append to history.
                    string newlyVisible = ComputeDeltaSincePrev(_prevMirror, system);

                    _mirror = system;
                    _prevMirror = system;

                    if (newlyVisible.Length > 0)
                    {
                        _history.Append(newlyVisible);

                        // Safety cap for runaway sessions: drop from the FRONT of the history buffer.
                        if (_history.Length > MAX_HISTORY_CHARS)
                        {
                            int keep = (int)(MAX_HISTORY_CHARS * 0.9);
                            int remove = _history.Length - keep;
                            if (remove > 0)
                            {
                                // Remove from front
                                var trimmed = _history.ToString(remove, keep);
                                _history.Clear();
                                _history.Append(trimmed);

                                // Adjust send pointer
                                _historySentPos = Math.Max(0, _historySentPos - remove);
                            }
                        }
                    }

                    // UI shows full history
                    uiTextToRaise = _history.ToString();
                }
            }

            if (uiTextToRaise != null)
            {
                Log("UPDATE", system, $"mirror={_mirror.Length}, history={uiTextToRaise.Length}");
                NotifyUI(uiTextToRaise);
            }
        }

        // ---------- Delta helpers ----------

        // Normalize LC text to avoid invisible-mismatch issues
        private static string NormalizeSystemText(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Remove zero-width and soft hyphen artifacts
            s = s.Replace("\u200B", string.Empty) // ZWSP
                 .Replace("\uFEFF", string.Empty) // ZWNBSP/BOM
                 .Replace("\u00AD", string.Empty) // SHY
                 .Replace("\u200C", string.Empty) // ZWNJ
                 .Replace("\u200D", string.Empty); // ZWJ

            // Normalize line endings to '\n'
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");

            return s;
        }

        /// <summary>
        /// Compute what is truly "newly visible" when moving from prevMirror to currMirror.
        /// Robust against:
        ///   - Pure append
        ///   - Tail rewrites (small corrections at the end)
        ///   - Head trims (rolling buffer drops the beginning)
        /// Strategy:
        ///   1) If curr starts with prev → return curr[prev.Length..]
        ///   2) Head-align suffix(prev) with prefix(curr) → return curr[overlap..]
        ///   3) Anywhere-anchor: find the longest suffix(prev) (≤ OVERLAP_PROBE) inside curr,
        ///      return the part after that anchor.
        ///   4) Otherwise, return empty (treat as reset/correction without new tail).
        /// </summary>
        private static string ComputeDeltaSincePrev(string prevMirror, string currMirror)
        {
            if (string.IsNullOrEmpty(currMirror)) return string.Empty;
            if (string.IsNullOrEmpty(prevMirror)) return currMirror;

            // 1) Pure append
            if (currMirror.StartsWith(prevMirror, StringComparison.Ordinal))
                return currMirror.Substring(prevMirror.Length);

            // 2) Head alignment
            {
                int probePrev = Math.Min(prevMirror.Length, OVERLAP_PROBE);
                int probeCurr = Math.Min(currMirror.Length, OVERLAP_PROBE);
                ReadOnlySpan<char> tail = prevMirror.AsSpan(prevMirror.Length - probePrev, probePrev);
                ReadOnlySpan<char> head = currMirror.AsSpan(0, probeCurr);
                int overlap = LongestSuffixPrefix(tail, head);
                if (overlap >= MIN_TRUSTED_OVERLAP && overlap < currMirror.Length)
                    return currMirror.Substring(overlap);
            }

            // 3) Anywhere-anchor (handles head trims)
            {
                int maxK = Math.Min(OVERLAP_PROBE, Math.Min(prevMirror.Length, currMirror.Length));
                for (int k = maxK; k >= MIN_TRUSTED_OVERLAP; k--)
                {
                    string pat = prevMirror.Substring(prevMirror.Length - k, k);
                    int idx = currMirror.IndexOf(pat, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        int after = idx + k;
                        if (after <= currMirror.Length)
                            return currMirror.Substring(after);
                    }
                }
            }

            // 4) Nothing confidently new
            return string.Empty;
        }

        private static int LongestSuffixPrefix(ReadOnlySpan<char> aSuffix, ReadOnlySpan<char> bPrefix)
        {
            int max = Math.Min(aSuffix.Length, bPrefix.Length);
            for (int len = max; len > 0; len--)
            {
                if (aSuffix.Slice(aSuffix.Length - len, len).SequenceEqual(bPrefix.Slice(0, len)))
                    return len;
            }
            return 0;
        }

        // ---------- Helpers ----------

        private void OnTextChanged(object sender, AutomationEventArgs e)
        {
            // Fire immediately; avoids waiting for next 200ms tick
            _tick?.Change(0, POLL_INTERVAL_MS);
        }

        private string ReadSystemTextSafely()
        {
            string txt = string.Empty;
            try
            {
                TryScrollToBottom();
                txt = _textPattern?.DocumentRange.GetText(-1) ?? string.Empty;
            }
            catch
            {
                try { txt = _textPattern?.DocumentRange.GetText(-1) ?? string.Empty; }
                catch { txt = string.Empty; }
            }
            return txt;
        }

        private void TryOccasionalScroll()
        {
            try
            {
                _scrollTickMod++;
                if (_textContainer == null) return;
                if (_scrollTickMod % 10 != 0) return; // ~2s cadence

                if (_textContainer.TryGetCurrentPattern(ScrollPattern.Pattern, out var spObj))
                {
                    var sp = spObj as ScrollPattern;
                    if (sp != null && sp.Current.VerticallyScrollable)
                    {
                        if (sp.Current.VerticalScrollPercent < 99.0)
                        {
                            sp.SetScrollPercent(sp.Current.HorizontallyScrollable ? 100 : ScrollPattern.NoScroll, 100);
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        private void TryScrollToBottom()
        {
            try
            {
                if (_textContainer != null &&
                    _textContainer.TryGetCurrentPattern(ScrollPattern.Pattern, out var spObj))
                {
                    var sp = spObj as ScrollPattern;
                    if (sp != null && sp.Current.VerticallyScrollable)
                    {
                        sp.SetScrollPercent(sp.Current.HorizontallyScrollable ? 100 : ScrollPattern.NoScroll, 100);
                        Thread.Sleep(50);
                    }
                }
            }
            catch { }
        }

        private void NotifyUI(string text)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnNewText?.Invoke(text);
                }));
            }
            catch { }
        }

        private string GetHistoryUnsafe()
        {
            // Call only under _stateLock OR immediately after building.
            return _history.ToString();
        }

        private void Log(string evt, string system, string info)
        {
            try
            {
                lock (_logLock)
                {
                    var sb = new StringBuilder();
                    lock (_stateLock)
                    {
                        sb.AppendLine($"=== {evt} - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
                        sb.AppendLine($"System len: {system?.Length ?? 0}");
                        sb.AppendLine($"Mirror len: {_mirror.Length}");
                        sb.AppendLine($"History len: {_history.Length}");
                        sb.AppendLine($"SentPos: {_historySentPos}");
                        sb.AppendLine($"Info: {info}");
                        sb.AppendLine();
                    }
                    File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }

        // --- Window discovery ---

        private AutomationElement? FindLiveCaptionsWindow()
        {
            try
            {
                var root = AutomationElement.RootElement;
                var cond = new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                    new PropertyCondition(AutomationElement.IsWindowPatternAvailableProperty, true)
                );

                var windows = root.FindAll(TreeScope.Children, cond);
                foreach (AutomationElement w in windows)
                {
                    string name = w.Current.Name ?? "";
                    if (name.IndexOf("Live captions", StringComparison.OrdinalIgnoreCase) >= 0)
                        return w;
                }
            }
            catch { }
            return null;
        }

        private AutomationElement? FindTextContainer(AutomationElement window)
        {
            try
            {
                var cond = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)
                );

                var textEl = window.FindFirst(TreeScope.Descendants, cond);
                if (textEl != null && textEl.TryGetCurrentPattern(TextPattern.Pattern, out _))
                    return textEl;

                var all = window.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                foreach (AutomationElement el in all)
                {
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                    {
                        try
                        {
                            var tp = pat as TextPattern;
                            string t = tp?.DocumentRange.GetText(-1) ?? "";
                            if (!string.IsNullOrEmpty(t) &&
                                !t.Equals("Untitled", StringComparison.OrdinalIgnoreCase) &&
                                t.Length > 10)
                                return el;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private bool TryLaunchLiveCaptions()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:accessibility-hearing",
                    UseShellExecute = true
                });
            }
            catch
            {
                return false;
            }
            return true;
        }

        private void TryMinimizeWindow(AutomationElement? w)
        {
            if (w == null) return;
            try
            {
                if (w.TryGetCurrentPattern(WindowPattern.Pattern, out var p))
                {
                    var wp = p as WindowPattern;
                    if (wp != null && wp.Current.CanMinimize)
                        wp.SetWindowVisualState(WindowVisualState.Minimized);
                }
            }
            catch { }
        }
    }
}
