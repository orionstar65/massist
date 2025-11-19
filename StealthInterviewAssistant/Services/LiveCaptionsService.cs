// LiveCaptionsService.cs
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

        // Previous system snapshot (normalized)
        private string _lastSnapshot = string.Empty;

        // Public-facing: full, unbounded transcript for your app
        private readonly StringBuilder _history = new StringBuilder(32_768);
        private int _lastSentPos = 0; // index into history for hotkey delta
        private string? _cachedHistoryString = null; // Cache for ToString() result
        private int _cachedHistoryLength = 0; // Track when cache is invalid

        // UIA
        private AutomationElement? _liveCaptionsWindow;
        private AutomationElement? _textContainer;
        private TextPattern? _textPattern;
        private AutomationEventHandler? _textChangedHandler;

        // Loop
        private System.Threading.Timer? _tick;
        private bool _isRunning = false;

        // Reentrancy guard for Tick
        private int _inTick = 0;

        // Tunables
        private const int POLL_INTERVAL_MS = 160; // snappy but light
        private const int MAX_OVERLAP = 4096;     // search deeper to avoid hard realigns
        private const int MIN_NEW_CHARS = 0;      // allow 0 on frames where we only realign
        private const int MAX_HISTORY_SIZE = 51_200; // 100KB limit (51,200 chars for UTF-16, ~100KB)

        // Logging
        private readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "livecaption.log");
        private readonly object _logLock = new object();
        private long _frame = 0;
        private bool _isLoggingEnabled = false; // Default: logging disabled

        // Events
        public event Action<string>? OnNewText; // emits full history to your UI

        // Logging control
        public bool IsLoggingEnabled
        {
            get => _isLoggingEnabled;
            set => _isLoggingEnabled = value;
        }

        public void SetLoggingEnabled(bool enabled)
        {
            _isLoggingEnabled = enabled;
        }

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
                        for (int i = 0; i < 8 && _liveCaptionsWindow == null; i++)
                        {
                            _liveCaptionsWindow = FindLiveCaptionsWindow();
                            Thread.Sleep(700);
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

                    string initial = ReadSystemTextSafely();
                    string normInitial = Normalize(initial);

                    lock (_stateLock)
                    {
                        _history.Clear();
                        _history.Append(normInitial);
                        _lastSnapshot = normInitial;

                        _lastSentPos = _history.Length; // skip initial buffer on first send
                        _frame = 0;
                        _cachedHistoryString = null; // Clear cache on start
                        _cachedHistoryLength = 0;
                    }

                    NotifyFullHistoryToUI();

                    _isRunning = true;
                    _tick = new System.Threading.Timer(Tick, null, POLL_INTERVAL_MS, POLL_INTERVAL_MS);
                    TryMinimizeWindow(_liveCaptionsWindow);

                    LogFrame("START", normInitial, "init", appended: "", details: $"H={_history.Length}, lastSent={_lastSentPos}");
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

        /// <summary>Full, unbounded transcript text.</summary>
        public string GetAll()
        {
            lock (_stateLock)
            {
                // Use cache if history hasn't changed
                if (_cachedHistoryString != null && _cachedHistoryLength == _history.Length)
                {
                    return _cachedHistoryString;
                }
                _cachedHistoryString = _history.ToString();
                _cachedHistoryLength = _history.Length;
                return _cachedHistoryString;
            }
        }

        /// <summary>Returns only the new text since last hotkey send.</summary>
        public string GetLastPart()
        {
            lock (_stateLock)
            {
                if (_lastSentPos >= _history.Length) return string.Empty;
                // Use cached string if available and valid
                if (_cachedHistoryString != null && _cachedHistoryLength == _history.Length && _lastSentPos == 0)
                {
                    return _cachedHistoryString;
                }
                // Only create substring if needed
                int length = _history.Length - _lastSentPos;
                if (length <= 0) return string.Empty;
                return _history.ToString(_lastSentPos, length);
            }
        }

        /// <summary>Advance baseline to end of history.</summary>
        public void MarkLastPartAsSent()
        {
            lock (_stateLock)
            {
                _lastSentPos = _history.Length;
                Debug.WriteLine($"Marked as sent. sentPos={_lastSentPos}");
            }
        }

        /// <summary>Clear mirror & history, reset baseline.</summary>
        public void Clear()
        {
            lock (_stateLock)
            {
                _history.Clear();
                _lastSnapshot = string.Empty;
                _lastSentPos = 0;
                _frame = 0;
                _cachedHistoryString = null; // Clear cache
                _cachedHistoryLength = 0;
            }
            NotifyFullHistoryToUI();
            LogFrame("CLEAR", "", "clear", "", "Buffers cleared");
        }

        // ---------- Core loop ----------
        // --- Replace your current Tick with this one ---
        private void Tick(object? _)
        {
            if (!_isRunning || _textPattern == null) return;

            // Reentrancy guard
            if (Interlocked.Exchange(ref _inTick, 1) == 1) return;

            try
            {
                TryOccasionalScroll();

                string systemRaw;
                try { systemRaw = _textPattern.DocumentRange.GetText(-1) ?? string.Empty; }
                catch { return; }

                string snap = Normalize(systemRaw);
                if (string.IsNullOrEmpty(snap)) return;

                bool changed = false;
                string usedCase = "";
                string appendedPreview = "";
                string details = "";

                lock (_stateLock)
                {
                    // 0) Already aligned? (history tail equals snapshot)
                    if (EndsWithOrdinal(_history, snap))
                    {
                        usedCase = "aligned";
                        details = $"snap={snap.Length}, H={_history.Length}";
                    }
                    else
                    {
                        // 1) Find longest prefix of snapshot that appears in the tail of history.
                        //    If found at 'start', splice: history = history[..start] + snapshot
                        int searchWindow = Math.Max(snap.Length * 2, 4096); // deeper than snap
                        var (start, matchLen) = FindBestPrefixMatchStart(_history, snap, searchWindow);

                        if (start >= 0 && matchLen > 0)
                        {
                            // Splice from match start with the full snapshot
                            // Guarantees: history tail == snapshot and avoids duplication
                            ApplySplice(_history, start, snap);
                            changed = true;
                            usedCase = "splice-prefix-match";
                            appendedPreview = Preview(snap.Substring(matchLen)); // what effectively changed after the overlap
                            details = $"matchLen={matchLen}, start={start}, snap={snap.Length}, H={_history.Length}";
                        }
                        else
                        {
                            // 2) No overlap found. Hard realign once (avoid duplicates if repeated).
                            if (!EndsWithOrdinal(_history, snap))
                            {
                                if (_history.Length > 0 && _history[_history.Length - 1] != '\n')
                                    _history.Append('\n');
                                _history.Append(snap);
                                TrimHistoryIfNeeded(_history);
                                changed = true;
                                usedCase = "hard-realign";
                                appendedPreview = Preview(snap);
                                details = $"snap={snap.Length}, H={_history.Length}";
                            }
                            else
                            {
                                usedCase = "already-aligned";
                                details = $"snap={snap.Length}, H={_history.Length}";
                            }
                        }

                        // 3) Sanity: enforce invariant
                        if (!EndsWithOrdinal(_history, snap))
                        {
                            // Force fix (very rare)
                            int startFix = Math.Max(0, _history.Length - snap.Length);
                            ApplySplice(_history, startFix, snap);
                            usedCase += "+fix";
                        }
                        
                        // 4) Ensure history doesn't exceed size limit
                        TrimHistoryIfNeeded(_history);
                    }

                    _lastSnapshot = snap;
                    _frame++;
                    
                    // Invalidate cache when history changes
                    if (changed)
                    {
                        _cachedHistoryString = null;
                        _cachedHistoryLength = 0;
                    }

                    LogFrame("UPDATE", snap, usedCase, appendedPreview, details);
                }

                if (changed) NotifyFullHistoryToUI();
            }
            finally
            {
                Interlocked.Exchange(ref _inTick, 0);
            }
        }

        // --- Add these helpers ---

        /// <summary>
        /// Find the longest k (1..min(snap, window)) such that
        /// history tail window contains snap[0..k) and return the *last* occurrence
        /// start index in history. If found, return (start, k). Else (-1, 0).
        /// Strategy: search longer prefixes first; prefer the latest occurrence
        /// in the window to minimize deletions.
        /// </summary>
        private static (int start, int matchLen) FindBestPrefixMatchStart(StringBuilder history, string snapshot, int maxWindow)
        {
            if (snapshot.Length == 0 || history.Length == 0) return (-1, 0);

            int winLen = Math.Min(history.Length, maxWindow);
            int winStart = history.Length - winLen;

            // Extract window as string for efficient search
            string hwin = history.ToString(winStart, winLen);

            int maxLen = Math.Min(snapshot.Length, winLen);
            for (int len = maxLen; len > 0; len--)
            {
                string pref = snapshot.Substring(0, len);
                int idx = hwin.LastIndexOf(pref, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int absStart = winStart + idx;
                    return (absStart, len);
                }
            }

            return (-1, 0);
        }

        /// <summary>
        /// Replace history from 'start' to end with 'replacement'.
        /// Equivalent to: history = history[..start] + replacement
        /// </summary>
        private void ApplySplice(StringBuilder history, int start, string replacement)
        {
            if (start < 0) start = 0;
            if (start > history.Length) start = history.Length;
            history.Remove(start, history.Length - start);
            history.Append(replacement);
            TrimHistoryIfNeeded(history);
        }

        /// <summary>
        /// Trim history to MAX_HISTORY_SIZE by removing oldest content.
        /// Updates _lastSentPos to maintain correct delta tracking.
        /// </summary>
        private void TrimHistoryIfNeeded(StringBuilder history)
        {
            if (history.Length <= MAX_HISTORY_SIZE) return;

            int excess = history.Length - MAX_HISTORY_SIZE;
            // Keep at least 80% of max size to avoid frequent trims
            int trimAmount = Math.Max(excess, MAX_HISTORY_SIZE / 5);
            
            // Remove oldest content
            history.Remove(0, trimAmount);
            
            // Invalidate cache since history changed
            _cachedHistoryString = null;
            _cachedHistoryLength = 0;
            
            // Adjust _lastSentPos to account for trimmed content
            if (_lastSentPos > trimAmount)
            {
                _lastSentPos -= trimAmount;
            }
            else
            {
                // If _lastSentPos was in the trimmed region, reset to start
                _lastSentPos = 0;
            }
        }

        /// <summary>Check if StringBuilder ends with string (ordinal).</summary>
        private static bool EndsWithOrdinal(StringBuilder sb, string s)
        {
            if (s.Length == 0) return true;
            if (sb.Length < s.Length) return false;
            int start = sb.Length - s.Length;
            for (int i = 0; i < s.Length; i++)
                if (sb[start + i] != s[i]) return false;
            return true;
        }


        // ---------- Tail-alignment helpers ----------

        /// <summary>
        /// Find the longest k such that suffix(history, k) == prefix(snapshot, k),
        /// searching up to maxOverlap characters.
        /// </summary>
        private static int LongestSuffixPrefixTailVsHead(StringBuilder history, string snapshot, int maxOverlap)
        {
            int max = Math.Min(Math.Min(history.Length, snapshot.Length), maxOverlap);
            for (int len = max; len > 0; len--)
            {
                // compare suffix of history with prefix of snapshot
                int hStart = history.Length - len;
                bool match = true;
                for (int i = 0; i < len; i++)
                {
                    if (history[hStart + i] != snapshot[i]) { match = false; break; }
                }
                if (match) return len;
            }
            return 0;
        }

        // ---------- Normalization (minimal to avoid data loss) ----------
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Keep content stable. Only normalize newlines and spaces-before-newline.
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[ ]+\n", "\n");
            // Do NOT collapse spaces/tabs or squeeze multiple newlines; WLC may rely on them.
            return s;
        }

        // ---------- UIA helpers ----------
        private void OnTextChanged(object sender, AutomationEventArgs e)
        {
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
                if (_textContainer == null) return;
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
                        Thread.Sleep(40);
                    }
                }
            }
            catch { }
        }

        private void NotifyFullHistoryToUI()
        {
            try
            {
                string payload;
                lock (_stateLock)
                {
                    // Use cache if available
                    if (_cachedHistoryString != null && _cachedHistoryLength == _history.Length)
                    {
                        payload = _cachedHistoryString;
                    }
                    else
                    {
                        payload = _history.ToString();
                        _cachedHistoryString = payload;
                        _cachedHistoryLength = _history.Length;
                    }
                }
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => OnNewText?.Invoke(payload)));
            }
            catch { }
        }

        // ---------- Logging ----------
        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var clean = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return clean.Length > 200 ? clean.Substring(0, 200) + "..." : clean;
        }

        private void LogFrame(string evt, string system, string used, string appended, string details)
        {
            if (!_isLoggingEnabled) return; // Skip logging if disabled

            try
            {
                lock (_logLock)
                {
                    var sb = new StringBuilder();
                    lock (_stateLock)
                    {
                        sb.AppendLine($"=== {evt} #{_frame} - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
                        sb.AppendLine($"Used: {used}");
                        sb.AppendLine($"System len: {system?.Length ?? 0}");
                        sb.AppendLine($"System head: {Preview(system)}");
                        sb.AppendLine($"History len: {_history.Length}");
                        sb.AppendLine($"LastSentPos: {_lastSentPos}");
                        sb.AppendLine($"Appended len: {appended?.Length ?? 0}");
                        sb.AppendLine($"Appended head: {Preview(appended)}");
                        sb.AppendLine($"Details: {details}");
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
                return true;
            }
            catch { return false; }
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
