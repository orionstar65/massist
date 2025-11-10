using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace StealthInterviewAssistant.Interop
{
    public static class NativeMethods
    {
        // Constants
        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCHITTEST = 0x0084;
        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const int DWMWA_EXCLUDED_FROM_CAPTURE = 25;
        public const int DWMWA_CLOAK = 14;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const uint WDA_NONE = 0x0;
        
        // Hit test constants
        public const int HTCLIENT = 1;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;

        public const int SRCCOPY    = 0x00CC0020;
        public const int CAPTUREBLT = 0x40000000;

        // Virtual screen metrics (multi-monitor)
        public const int SM_XVIRTUALSCREEN  = 76;
        public const int SM_YVIRTUALSCREEN  = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        // RegisterHotKey / UnregisterHotKey
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // SendMessage for handling WM_HOTKEY
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // GetWindowLong / SetWindowLong for window styles
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out System.Drawing.Rectangle lpRect);

        // Low-level keyboard hook for continuous key detection
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LSHIFT = 0xA0;
        public const int VK_RSHIFT = 0xA1;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // Screenshot functions
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool BitBlt(
            IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hDC, uint nFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // ShowWindow constants
        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_SHOWMINNOACTIVE = 7;
        public const int SW_SHOWNA = 8;
        public const int SW_RESTORE = 9;

        public const uint SWP_NOSIZE     = 0x0001;
        public const uint SWP_NOMOVE     = 0x0002;
        public const uint SWP_NOZORDER   = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        // DWM attributes
        public const int DWMWA_CLOAKED  = 14;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // Window style constants
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;

        // Clipboard functions
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseClipboard();

        // DwmSetWindowAttribute
        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // DwmGetWindowAttribute
        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int pvAttribute, int cbAttribute);

        // SetWindowDisplayAffinity
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

        // Helper Methods

        /// <summary>
        /// Sets whether a window should be excluded from screen capture.
        /// </summary>
        public static bool SetExcludedFromCapture(Window window, bool enabled)
        {
            if (window == null)
                return false;

            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;

            if (hwnd == IntPtr.Zero)
                return false;

            return SetExcludedFromCapture(hwnd, enabled);
        }

        /// <summary>
        /// Sets whether a window (by handle) should be excluded from screen capture.
        /// Uses BOTH DwmSetWindowAttribute and SetWindowDisplayAffinity for maximum compatibility.
        /// Note: Some capture applications may ignore one method or the other; using both improves coverage
        /// across different screen capture tools and APIs.
        /// </summary>
        public static bool SetExcludedFromCapture(IntPtr hwnd, bool enabled)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            bool success1 = false;
            bool success2 = false;

            // Method 1: DwmSetWindowAttribute with DWMWA_EXCLUDED_FROM_CAPTURE
            int value = enabled ? 1 : 0;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_EXCLUDED_FROM_CAPTURE, ref value, sizeof(int));
            success1 = result == 0; // S_OK

            // Method 2: SetWindowDisplayAffinity with WDA_EXCLUDEFROMCAPTURE
            if (enabled)
            {
                success2 = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            }
            else
            {
                // To disable, set affinity to 0 (WDA_NONE)
                success2 = SetWindowDisplayAffinity(hwnd, WDA_NONE);
            }

            // Return true if at least one method succeeded
            return success1 || success2;
        }

        /// <summary>
        /// Cloaks or uncloaks a window (makes it invisible to screen capture).
        /// </summary>
        public static bool CloakWindow(Window window, bool enabled)
        {
            if (window == null)
                return false;

            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;

            if (hwnd == IntPtr.Zero)
                return false;

            int value = enabled ? 1 : 0;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref value, sizeof(int));
            return result == 0; // S_OK
        }

        /// <summary>
        /// Registers a global hotkey using WindowInteropHelper.
        /// </summary>
        public static bool RegisterGlobalHotkey(WindowInteropHelper helper, int id, uint modifiers, Keys key)
        {
            if (helper == null)
                return false;

            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            return RegisterHotKey(hwnd, id, modifiers, (uint)key);
        }

        /// <summary>
        /// Unregisters a global hotkey.
        /// </summary>
        public static bool UnregisterGlobalHotkey(WindowInteropHelper helper, int id)
        {
            if (helper == null)
                return false;

            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            return UnregisterHotKey(hwnd, id);
        }

        /// <summary>
        /// Convenience method to register Ctrl+Alt+C hotkey.
        /// </summary>
        public static bool RegisterGlobalHotkey(WindowInteropHelper helper, int id = 1)
        {
            return RegisterGlobalHotkey(helper, id, MOD_CONTROL | MOD_ALT, Keys.C);
        }
    }
}

