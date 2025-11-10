using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace StealthInterviewAssistant.Interop
{
    /// <summary>
    /// Hides the Live Captions window from screen while keeping it running,
    /// and removes its taskbar button using an owner-window trick.
    /// </summary>
    public static class LiveCaptionsTaskbarHider
    {
        // Keep a tiny hidden owner so LC doesn't show on taskbar
        private static IntPtr _ownerHwnd = IntPtr.Zero;

        // Track LC window + original placement for restore
        private static IntPtr _lcHwnd = IntPtr.Zero;
        private static RECT _originalRect;
        private static bool _hasOriginalRect = false;

        // You can change this if Windows uses a different title in your locale
        private const string LiveCaptionsTitle = "Live captions";

        // Public API ----------------------------------------------------------

        /// <summary>
        /// Attempts to find Live Captions window and prepare hiding (owner window + off-screen park).
        /// Safe to call multiple times. Returns true if LC window was found (and prepared).
        /// </summary>
        public static bool EnsurePrepared()
        {
            // Find/refresh LC hwnd
            _lcHwnd = FindLiveCaptionsHwnd();
            if (_lcHwnd == IntPtr.Zero)
                return false;

            // Create (or keep) tiny toolwindow owner
            if (_ownerHwnd == IntPtr.Zero)
                _ownerHwnd = CreateHiddenOwnerWindow();

            if (_ownerHwnd != IntPtr.Zero)
            {
                // Set owner so LC doesn't show a taskbar button
                // This can silently fail for some system windows; that's OK.
                SetWindowLongPtr(_lcHwnd, GWLP_HWNDPARENT, _ownerHwnd);
            }

            // Cache original rect (once) for restore
            if (!_hasOriginalRect && GetWindowRect(_lcHwnd, out var rc))
            {
                _originalRect = rc;
                _hasOriginalRect = true;
            }

            return true;
        }

        /// <summary>
        /// Hide LC from the screen (keep it running) and remove taskbar icon.
        /// Also cloaks the window to make it undetectable from screen capture.
        /// </summary>
        public static bool HideKeepRunning()
        {
            if (!EnsurePrepared()) return false;

            // Park off-screen (classic "parking lot")
            // (-32000,-32000) is a well-known off-screen location Windows uses for minimized/parked windows.
            const int OffX = -32000;
            const int OffY = -32000;

            // 1×1 size; keep window visible so LC keeps streaming
            SetWindowPos(_lcHwnd, IntPtr.Zero, OffX, OffY, 1, 1,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);

            // Cloak the window to make it undetectable from screen capture
            // This hides it from PrintWindow, BitBlt, and other capture APIs
            DwmSetWindowAttribute(_lcHwnd, DWMWA_CLOAK, ref TrueInt, sizeof(int));

            return true;
        }

        /// <summary>
        /// Brings LC back to its original position/size and ensures the taskbar entry is still hidden.
        /// Also uncloaks the window so it's visible and detectable again.
        /// </summary>
        public static bool Show()
        {
            if (_lcHwnd == IntPtr.Zero)
            {
                _lcHwnd = FindLiveCaptionsHwnd();
                if (_lcHwnd == IntPtr.Zero) return false;
            }

            // Uncloak the window so it's visible and detectable from screen capture
            DwmSetWindowAttribute(_lcHwnd, DWMWA_CLOAK, ref FalseInt, sizeof(int));

            // Restore size/pos if we have it
            if (_hasOriginalRect)
            {
                int w = Math.Max(100, _originalRect.Right - _originalRect.Left);
                int h = Math.Max(60, _originalRect.Bottom - _originalRect.Top);
                SetWindowPos(_lcHwnd, IntPtr.Zero, _originalRect.Left, _originalRect.Top, w, h,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
            }
            else
            {
                // Just show somewhere visible if we don't have a rect
                SetWindowPos(_lcHwnd, IntPtr.Zero, 60, 60, 800, 250,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
            }

            // Keep owner attached (prevents taskbar button from appearing)
            if (_ownerHwnd == IntPtr.Zero)
                _ownerHwnd = CreateHiddenOwnerWindow();

            if (_ownerHwnd != IntPtr.Zero)
                SetWindowLongPtr(_lcHwnd, GWLP_HWNDPARENT, _ownerHwnd);

            return true;
        }

        /// <summary>
        /// Returns true if we currently have an LC hwnd and an owner set.
        /// </summary>
        public static bool IsActive => _lcHwnd != IntPtr.Zero && _ownerHwnd != IntPtr.Zero;

        /// <summary>
        /// Checks if the window is currently hidden (cloaked and off-screen).
        /// </summary>
        public static bool IsHidden()
        {
            if (_lcHwnd == IntPtr.Zero)
            {
                _lcHwnd = FindLiveCaptionsHwnd();
                if (_lcHwnd == IntPtr.Zero) return false;
            }

            // Check if window is off-screen (hidden)
            if (GetWindowRect(_lcHwnd, out var rc))
            {
                return rc.Left <= -10000 || rc.Top <= -10000 || (rc.Right - rc.Left) <= 10 || (rc.Bottom - rc.Top) <= 10;
            }
            return false;
        }

        /// <summary>
        /// Temporarily restores the window to a visible state so it can be found by UIA.
        /// Also uncloaks it temporarily. Returns true if the window was restored (or was already visible).
        /// </summary>
        public static bool RestoreTemporarily()
        {
            if (_lcHwnd == IntPtr.Zero)
            {
                _lcHwnd = FindLiveCaptionsHwnd();
                if (_lcHwnd == IntPtr.Zero) return false;
            }

            // Uncloak temporarily so UIA can find and access it
            DwmSetWindowAttribute(_lcHwnd, DWMWA_CLOAK, ref FalseInt, sizeof(int));

            // Check if window is off-screen (hidden)
            if (GetWindowRect(_lcHwnd, out var rc))
            {
                // If window is at off-screen position, restore it
                if (rc.Left <= -10000 || rc.Top <= -10000 || (rc.Right - rc.Left) <= 10 || (rc.Bottom - rc.Top) <= 10)
                {
                    // Restore to a visible position temporarily
                    SetWindowPos(_lcHwnd, IntPtr.Zero, 60, 60, 800, 250,
                        SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
                    
                    // Keep owner attached (prevents taskbar button from appearing)
                    if (_ownerHwnd == IntPtr.Zero)
                        _ownerHwnd = CreateHiddenOwnerWindow();
                    
                    if (_ownerHwnd != IntPtr.Zero)
                        SetWindowLongPtr(_lcHwnd, GWLP_HWNDPARENT, _ownerHwnd);
                    
                    return true;
                }
            }
            
            return true; // Window is already visible
        }

        // Internals ----------------------------------------------------------

        private static IntPtr FindLiveCaptionsHwnd()
        {
            // 1) Try UIA first (most reliable across Windows builds)
            try
            {
                var root = AutomationElement.RootElement;
                var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
                var windows = root.FindAll(TreeScope.Children, cond);
                foreach (AutomationElement w in windows)
                {
                    try
                    {
                        var name = w.Current.Name ?? string.Empty;
                        if (name.IndexOf(LiveCaptionsTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return new IntPtr(w.Current.NativeWindowHandle);
                        }
                    }
                    catch { /* ignore bad elements */ }
                }
            }
            catch { /* UIA unavailable? */ }

            // 2) Fallback: EnumWindows + title match
            IntPtr found = IntPtr.Zero;
            EnumWindows((hwnd, l) =>
            {
                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (!string.IsNullOrEmpty(title) &&
                    title.IndexOf(LiveCaptionsTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        private static IntPtr CreateHiddenOwnerWindow()
        {
            // WS_EX_TOOLWINDOW so it never shows on taskbar or Alt+Tab
            // Position far off-screen; visible to be a valid owner; 1×1 size.
            IntPtr hInstance = GetModuleHandle(null);
            // We’ll reuse the STATIC class to avoid registering a custom class.
            IntPtr hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW,
                "STATIC",
                "",
                WS_POPUP | WS_VISIBLE,
                -20000, -20000, 1, 1,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            return hwnd;
        }

        // P/Invoke -----------------------------------------------------------

        private const int GWLP_HWNDPARENT = -8;

        private const int SWP_NOSIZE = 0x0001;
        private const int SWP_NOMOVE = 0x0002;
        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_SHOWWINDOW = 0x0040;
        private const int SWP_NOSENDCHANGING = 0x0400;

        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        // DWM (optional cloak)
        private const int DWMWA_CLOAK = 13;
        private static int TrueInt = 1;
        private static int FalseInt = 0;

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtrNative(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        // Overload helpers for x86/x64
        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtrNative(hWnd, index, value);
            else
            {
                int res = SetWindowLong32(hWnd, index, value.ToInt32());
                return new IntPtr(res);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string? lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
    }
}
