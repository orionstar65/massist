using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using StealthInterviewAssistant.Interop;
using StealthInterviewAssistant.ViewModels;

namespace StealthInterviewAssistant.Views
{
    public partial class MainWindow : Window
    {
        private const int HOTKEY_ID_SEND_TO_WEBVIEW = 1;
        private const int HOTKEY_ID_MOVE_LEFT = 2;
        private const int HOTKEY_ID_MOVE_RIGHT = 3;
        private const int HOTKEY_ID_MOVE_UP = 4;
        private const int HOTKEY_ID_MOVE_DOWN = 5;
        private const int HOTKEY_ID_WIDTH_DECREASE = 6;
        private const int HOTKEY_ID_WIDTH_INCREASE = 7;
        private const int HOTKEY_ID_HEIGHT_DECREASE = 8;
        private const int HOTKEY_ID_HEIGHT_INCREASE = 9;
        private const int HOTKEY_ID_TOGGLE_VISIBLE = 10;
        private const int HOTKEY_ID_EXIT = 11;
        private const int HOTKEY_ID_OPACITY_INCREASE = 12;
        private const int HOTKEY_ID_OPACITY_DECREASE = 13;
        private const int HOTKEY_ID_SCREENSHOT = 14;
        
        private const double OPACITY_STEP = 0.05;
        private const double OPACITY_MIN = 0.1;
        private const double OPACITY_MAX = 1.0; // Maximum opacity set to 100% (no transparency)
        
        private HwndSource? _hwndSource;
        private WindowInteropHelper? _helper;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
        private System.Windows.Threading.DispatcherTimer? _moveTimer;
        private System.Windows.Threading.DispatcherTimer? _resizeTimer;
        private int _currentMoveX = 0;
        private int _currentMoveY = 0;
        private int _currentResizeWidth = 0;
        private int _currentResizeHeight = 0;
        private HelpWindow? _helpWindow;
        private const int MOVE_STEP = 8; // pixels to move per tick (increased for faster movement)
        private const int RESIZE_STEP = 10; // pixels to resize per tick (increased for faster resizing)
        private const int TIMER_INTERVAL_MS = 10; // ~100fps for faster, smoother movement
        
        // Lock to prevent concurrent hotkey sends
        private readonly object _sendLock = new object();
        private bool _isSending = false;
        
        // Retry counter for hotkey registration
        private int _hotkeyRegistrationRetryCount = 0;
        private const int MAX_HOTKEY_REGISTRATION_RETRIES = 5;
        
        // Cursor system state
        private enum CursorSystem { Arrow, Caret, Normal }
        private CursorSystem _currentCursorSystem = CursorSystem.Normal;
        private CursorSystem _previousCursorSystem = CursorSystem.Normal; // Track previous system to detect changes
        
        // Cursor update caching
        private readonly Dictionary<System.Windows.DependencyObject, System.Windows.Input.Cursor?> _cursorCache = new Dictionary<System.Windows.DependencyObject, System.Windows.Input.Cursor?>();
        private System.Windows.Input.Cursor? _lastAppliedCursor = null;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            _helper = new WindowInteropHelper(this);
            
            // Wait for handle to be ready - retry if not available yet
            if (_helper.Handle == IntPtr.Zero)
            {
                // Retry after a short delay
                Dispatcher.BeginInvoke(new Action(() => InitializeHotkeysAndHooks()), 
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            
            InitializeHotkeysAndHooks();
        }

        /// <summary>
        /// Initializes hotkeys, hooks, and related functionality once window handle is ready.
        /// </summary>
        private void InitializeHotkeysAndHooks()
        {
            if (_helper == null || _helper.Handle == IntPtr.Zero)
            {
                _hotkeyRegistrationRetryCount++;
                if (_hotkeyRegistrationRetryCount < MAX_HOTKEY_REGISTRATION_RETRIES)
                {
                    // Retry after a short delay
                    System.Diagnostics.Debug.WriteLine(
                        $"Window handle not ready, retrying hotkey registration ({_hotkeyRegistrationRetryCount}/{MAX_HOTKEY_REGISTRATION_RETRIES})...");
                    Dispatcher.BeginInvoke(new Action(() => InitializeHotkeysAndHooks()), 
                        System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        "Failed to initialize hotkeys: Window handle not available after maximum retries. " +
                        "Some hotkey functionality may not work.");
                }
                return;
            }
            
            // Reset retry counter on success
            _hotkeyRegistrationRetryCount = 0;

            // Create HwndSource only if handle is valid
            _hwndSource = HwndSource.FromHwnd(_helper.Handle);
            
            if (_hwndSource != null)
            {
                // Add HwndSourceHook for WM_HOTKEY
                _hwndSource.AddHook(WndProc);
            }

            // Register all global hotkeys with error checking
            RegisterAllHotkeys();
            
            // Hide from Alt+Tab and taskbar
            HideFromAltTab();
            
            // Install low-level keyboard hook for continuous key detection
            InstallKeyboardHook();
            
            // Initialize movement timers
            _moveTimer = new System.Windows.Threading.DispatcherTimer();
            _moveTimer.Interval = TimeSpan.FromMilliseconds(TIMER_INTERVAL_MS);
            _moveTimer.Tick += MoveTimer_Tick;
            
            _resizeTimer = new System.Windows.Threading.DispatcherTimer();
            _resizeTimer.Interval = TimeSpan.FromMilliseconds(TIMER_INTERVAL_MS);
            _resizeTimer.Tick += ResizeTimer_Tick;
        }

        /// <summary>
        /// Registers all global hotkeys with proper error checking and logging.
        /// </summary>
        private void RegisterAllHotkeys()
        {
            if (_helper == null || _helper.Handle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("Window handle not ready for hotkey registration.");
                return; // Don't retry here - InitializeHotkeysAndHooks handles retries
            }

            // Helper method to register with error checking
            bool RegisterHotKeySafe(int id, uint modifiers, uint vk, string description)
            {
                bool success = NativeMethods.RegisterHotKey(_helper.Handle, id, modifiers, vk);
                if (!success)
                {
                    int error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to register hotkey {description} (ID: {id}). Error code: {error}");
                    
                    // Error 1409 = ERROR_HOTKEY_ALREADY_REGISTERED
                    if (error == 1409)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Hotkey {description} is already registered by another application. " +
                            "The hotkey may not work until the other application releases it.");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully registered hotkey: {description}");
                }
                return success;
            }

            // Register all hotkeys with error checking
            RegisterHotKeySafe(HOTKEY_ID_SEND_TO_WEBVIEW,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                0xBF, "Ctrl+Shift+/ (Send to WebView)");

            RegisterHotKeySafe(HOTKEY_ID_MOVE_LEFT,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.J, "Ctrl+Shift+J (Move Left)");

            RegisterHotKeySafe(HOTKEY_ID_MOVE_RIGHT,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.L, "Ctrl+Shift+L (Move Right)");

            RegisterHotKeySafe(HOTKEY_ID_MOVE_UP,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.I, "Ctrl+Shift+I (Move Up)");

            RegisterHotKeySafe(HOTKEY_ID_MOVE_DOWN,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.K, "Ctrl+Shift+K (Move Down)");

            RegisterHotKeySafe(HOTKEY_ID_WIDTH_DECREASE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.R, "Ctrl+Shift+R (Width Decrease)");

            RegisterHotKeySafe(HOTKEY_ID_WIDTH_INCREASE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.T, "Ctrl+Shift+T (Width Increase)");

            RegisterHotKeySafe(HOTKEY_ID_HEIGHT_DECREASE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.Q, "Ctrl+Shift+Q (Height Decrease)");

            RegisterHotKeySafe(HOTKEY_ID_HEIGHT_INCREASE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.A, "Ctrl+Shift+A (Height Increase)");

            RegisterHotKeySafe(HOTKEY_ID_TOGGLE_VISIBLE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.H, "Ctrl+Shift+H (Toggle Visibility)");

            RegisterHotKeySafe(HOTKEY_ID_EXIT,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.P, "Ctrl+Shift+P (Exit)");

            RegisterHotKeySafe(HOTKEY_ID_OPACITY_INCREASE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.Up, "Ctrl+Shift+Up (Opacity Increase)");

            RegisterHotKeySafe(HOTKEY_ID_OPACITY_DECREASE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.Down, "Ctrl+Shift+Down (Opacity Decrease)");

            RegisterHotKeySafe(HOTKEY_ID_SCREENSHOT,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)System.Windows.Forms.Keys.OemPeriod, "Ctrl+Shift+. (Screenshot)");
        }

        private void InstallKeyboardHook()
        {
            _keyboardProc = KeyboardHookProc;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                if (curModule != null)
                {
                    _keyboardHook = NativeMethods.SetWindowsHookEx(
                        NativeMethods.WH_KEYBOARD_LL,
                        _keyboardProc,
                        NativeMethods.GetModuleHandle(curModule.ModuleName),
                        0);
                }
            }
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)hookStruct.vkCode;
                bool isKeyDown = wParam == (IntPtr)NativeMethods.WM_KEYDOWN;
                bool isKeyUp = wParam == (IntPtr)NativeMethods.WM_KEYUP;

                // Check if Ctrl+Shift are held using GetAsyncKeyState
                bool ctrlHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LCONTROL) & 0x8000) != 0 ||
                                (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RCONTROL) & 0x8000) != 0;
                bool shiftHeld = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LSHIFT) & 0x8000) != 0 ||
                                  (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RSHIFT) & 0x8000) != 0;

                if (ctrlHeld && shiftHeld)
                {
                    if (isKeyDown)
                    {
                        switch (vkCode)
                        {
                            case (int)System.Windows.Forms.Keys.J: // Left
                                _currentMoveX = -MOVE_STEP;
                                _currentMoveY = 0;
                                _moveTimer?.Start();
                                break;
                            case (int)System.Windows.Forms.Keys.L: // Right
                                _currentMoveX = MOVE_STEP;
                                _currentMoveY = 0;
                                _moveTimer?.Start();
                                break;
                            case (int)System.Windows.Forms.Keys.I: // Up
                                _currentMoveX = 0;
                                _currentMoveY = -MOVE_STEP;
                                _moveTimer?.Start();
                                break;
                            case (int)System.Windows.Forms.Keys.K: // Down
                                _currentMoveX = 0;
                                _currentMoveY = MOVE_STEP;
                                _moveTimer?.Start();
                                break;
                            case (int)System.Windows.Forms.Keys.R: // Width decrease
                                _currentResizeWidth = -RESIZE_STEP;
                                _currentResizeHeight = 0;
                                _resizeTimer?.Start();
                                break;
                            case (int)System.Windows.Forms.Keys.T: // Width increase
                                _currentResizeWidth = RESIZE_STEP;
                                _currentResizeHeight = 0;
                                _resizeTimer?.Start();
                                break;
                            case (int)System.Windows.Forms.Keys.Q: // Height decrease
                                _currentResizeWidth = 0;
                                _currentResizeHeight = -RESIZE_STEP;
                                _resizeTimer?.Start();
                                break;
                            case (int)System.Windows.Forms.Keys.A: // Height increase
                                _currentResizeWidth = 0;
                                _currentResizeHeight = RESIZE_STEP;
                                _resizeTimer?.Start();
                                break;
                        }
                    }
                    else if (isKeyUp)
                    {
                        // Stop timers when key is released
                        if (vkCode == (int)System.Windows.Forms.Keys.J || 
                            vkCode == (int)System.Windows.Forms.Keys.L ||
                            vkCode == (int)System.Windows.Forms.Keys.I ||
                            vkCode == (int)System.Windows.Forms.Keys.K)
                        {
                            _moveTimer?.Stop();
                            _currentMoveX = 0;
                            _currentMoveY = 0;
                        }
                        else if (vkCode == (int)System.Windows.Forms.Keys.R ||
                                 vkCode == (int)System.Windows.Forms.Keys.T ||
                                 vkCode == (int)System.Windows.Forms.Keys.Q ||
                                 vkCode == (int)System.Windows.Forms.Keys.A)
                        {
                            _resizeTimer?.Stop();
                            _currentResizeWidth = 0;
                            _currentResizeHeight = 0;
                        }
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void MoveTimer_Tick(object? sender, EventArgs e)
        {
            if (_moveTimer == null) return;
            if (_currentMoveX != 0 || _currentMoveY != 0)
            {
                MoveWindow(_currentMoveX, _currentMoveY);
            }
        }

        private void ResizeTimer_Tick(object? sender, EventArgs e)
        {
            if (_resizeTimer == null) return;
            if (_currentResizeWidth != 0 || _currentResizeHeight != 0)
            {
                ResizeWindow(_currentResizeWidth, _currentResizeHeight);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set excluded from capture by default
            NativeMethods.SetExcludedFromCapture(this, true);
            
            // Hide content panels initially, but keep MainBorder visible so label can show
            // Set MainBorder opacity to 1.0 so the label inside it is visible
            // Start with MainBorder at small scale (0.7) - it will zoom in during label animation
            
            // Ensure StartupLabelContainer is visible and ready - do this immediately
            if (StartupLabelContainer != null)
            {
                // Force visibility immediately
                StartupLabelContainer.Visibility = Visibility.Visible;
                StartupLabelContainer.Opacity = 0; // Will be animated to visible
                System.Windows.Controls.Panel.SetZIndex(StartupLabelContainer, 1000);
                
                // Force layout update
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StartupLabelContainer.UpdateLayout();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                
                // Ensure StartupLabel is also visible
                var startupLabel = this.FindName("StartupLabel") as System.Windows.Controls.TextBlock;
                if (startupLabel != null)
                {
                    startupLabel.Visibility = Visibility.Visible;
                    startupLabel.Opacity = 1.0; // Label should be fully visible (container opacity controls visibility)
                }
            }
            
            // Initialize BorderPath at small scale for animation
            var borderPath = this.FindName("BorderPath") as System.Windows.Shapes.Rectangle;
            if (borderPath != null)
            {
                var borderPathTransform = borderPath.RenderTransform as System.Windows.Media.ScaleTransform;
                if (borderPathTransform != null)
                {
                    borderPathTransform.ScaleX = 0.1;
                    borderPathTransform.ScaleY = 0.1;
                }
            }
            
            if (ContentGrid != null)
            {
                ContentGrid.Visibility = Visibility.Hidden;
            }

            // Initialize button groups container transform for animation
            var buttonGroups = this.FindName("ButtonGroupsContainer") as System.Windows.FrameworkElement;
            if (buttonGroups != null)
            {
                var buttonTransform = buttonGroups.RenderTransform as System.Windows.Media.TranslateTransform;
                if (buttonTransform == null)
                {
                    buttonTransform = new System.Windows.Media.TranslateTransform();
                    buttonTransform.Y = 20; // Start position for slide-down animation
                    buttonGroups.RenderTransform = buttonTransform;
                }
            }
            
            // Start the startup label animation immediately
            // Use a small delay to ensure window is fully rendered
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StartStartupAnimation();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            
            // Initialize cursor system to Normal (default) after UI is fully loaded
            // This ensures all cursors are correct on startup and NormalCursorButton is active
            // We do this after UI is loaded so all elements are available for cursor updates
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Force a full cursor system update to ensure all XAML-set cursors are overridden
                // Clear cache first to ensure we check all elements
                _cursorCache.Clear();
                SwitchCursorSystem(CursorSystem.Normal);
                
                // Initialize mode toggle button text (default is Interview Mode)
                if (ModeToggleButtonText != null)
                {
                    if (DataContext is MainViewModel viewModel)
                    {
                        ModeToggleButtonText.Text = viewModel.IsInterviewMode ? "I" : "N";
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            
            // Subscribe to property changes
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.ExcludedFromCapture))
                    {
                        NativeMethods.SetExcludedFromCapture(this, viewModel.ExcludedFromCapture);
                        // Also update WebView2 host window if initialized
                        ApplyWebView2Exclusion(viewModel.ExcludedFromCapture);
                    }
                    else if (args.PropertyName == nameof(MainViewModel.Transcript))
                    {
                        // Auto-scroll to bottom when transcript updates
                        // Mark that we need to scroll after layout
                        var captionTextBlock = this.FindName("CaptionTextBlock") as System.Windows.FrameworkElement;
                        if (captionTextBlock != null)
                        {
                            // Use a simple delayed scroll approach
                            Dispatcher.BeginInvoke(new Action(() => 
                            {
                                ScrollToBottom();
                                // Additional scroll after a short delay to ensure content is measured
                                var timer = new System.Windows.Threading.DispatcherTimer
                                {
                                    Interval = TimeSpan.FromMilliseconds(50)
                                };
                                timer.Tick += (sender, e) =>
                                {
                                    timer.Stop();
                                    ScrollToBottom();
                                };
                                timer.Start();
                            }), System.Windows.Threading.DispatcherPriority.Loaded);
                        }
                    }
                };
            }

            // Hook WebView2 initialization to apply exclusion to its host window
            if (StealthBrowser != null)
            {
                StealthBrowser.CoreWebView2InitializationCompleted += StealthBrowser_CoreWebView2InitializationCompleted;
                
                // Hook navigation events for better error handling
                StealthBrowser.NavigationCompleted += StealthBrowser_NavigationCompleted;
                
                // Initialize WebView2 if not already initialized
                if (StealthBrowser.CoreWebView2 == null)
                {
                    try
                    {
                        StealthBrowser.EnsureCoreWebView2Async();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"WebView2 initialization error: {ex.Message}");
                    }
                }
            }
        }

        private System.Windows.Media.Animation.Storyboard? _continuousBorderAnimation;

        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent, string name) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t && (child as System.Windows.FrameworkElement)?.Name == name)
                {
                    return t;
                }
                var childOfChild = FindVisualChild<T>(child, name);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        private void StartBorderAnimation()
        {
            try
            {
                // Small delay to ensure window is fully rendered
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Start continuous rotating light animation for border
                        // Removed: Neon border animation disabled
                        // var border = this.FindName("MainBorder") as System.Windows.FrameworkElement;
                        // if (border != null)
                        // {
                        //     StartContinuousBorderAnimation(border);
                        // }

                    // Animate left panel (slide in from left)
                    var leftPanel = this.FindName("LeftPanel") as System.Windows.FrameworkElement;
                    if (leftPanel != null)
                    {
                        var leftFade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.2),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        var leftSlide = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = -30,
                            To = 0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.2),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        leftPanel.BeginAnimation(System.Windows.UIElement.OpacityProperty, leftFade);
                        var leftTransform = leftPanel.RenderTransform as System.Windows.Media.TranslateTransform;
                        if (leftTransform != null)
                        {
                            leftTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, leftSlide);
                        }
                    }

                    // Animate right panel (slide in from right)
                    var rightPanel = this.FindName("RightPanel") as System.Windows.FrameworkElement;
                    if (rightPanel != null)
                    {
                        var rightFade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.3),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        var rightSlide = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 30,
                            To = 0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.3),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        rightPanel.BeginAnimation(System.Windows.UIElement.OpacityProperty, rightFade);
                        var rightTransform = rightPanel.RenderTransform as System.Windows.Media.TranslateTransform;
                        if (rightTransform != null)
                        {
                            rightTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, rightSlide);
                        }
                    }

                    // Animate split handler (fade in from middle to top and bottom)
                    var splitterRectangle = this.FindName("SplitterRectangle") as System.Windows.Shapes.Rectangle;
                    if (splitterRectangle != null)
                    {
                        // Create a gradient brush for the splitter (vertical gradient from top to bottom)
                        var gradientBrush = new System.Windows.Media.LinearGradientBrush();
                        gradientBrush.StartPoint = new System.Windows.Point(0, 0);
                        gradientBrush.EndPoint = new System.Windows.Point(0, 1);
                        gradientBrush.MappingMode = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox;

                        // Gradient: transparent at top, visible in middle, transparent at bottom
                        gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(0, 0x3F, 0x3F, 0x46), 0.0)); // Transparent at top
                        gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(80, 0x3F, 0x3F, 0x46), 0.4)); // Fade in
                        gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(180, 0x3F, 0x3F, 0x46), 0.5)); // Most visible in middle
                        gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(80, 0x3F, 0x3F, 0x46), 0.6)); // Fade out
                        gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(0, 0x3F, 0x3F, 0x46), 1.0)); // Transparent at bottom

                        // Set the gradient brush as the stroke brush
                        splitterRectangle.Stroke = gradientBrush;

                        var splitterFade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.8)),
                            BeginTime = TimeSpan.FromSeconds(0.4),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        var splitterScale = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.8)),
                            BeginTime = TimeSpan.FromSeconds(0.4),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        splitterRectangle.BeginAnimation(System.Windows.UIElement.OpacityProperty, splitterFade);
                        var splitterTransform = splitterRectangle.RenderTransform as System.Windows.Media.ScaleTransform;
                        if (splitterTransform != null)
                        {
                            splitterTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, splitterScale);
                        }
                    }

                    // Animate caption text block
                    var captionText = this.FindName("CaptionTextBlock") as System.Windows.FrameworkElement;
                    if (captionText != null)
                    {
                        var textFade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.5)),
                            BeginTime = TimeSpan.FromSeconds(0.5),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        captionText.BeginAnimation(System.Windows.UIElement.OpacityProperty, textFade);
                    }

                    // Animate button groups (fade down)
                    var buttonGroups = this.FindName("ButtonGroupsContainer") as System.Windows.FrameworkElement;
                    if (buttonGroups != null)
                    {
                        buttonGroups.Visibility = System.Windows.Visibility.Visible;
                        var buttonFade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.6),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        var buttonSlide = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 20,
                            To = 0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.6),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        buttonGroups.BeginAnimation(System.Windows.UIElement.OpacityProperty, buttonFade);
                        var buttonTransform = buttonGroups.RenderTransform as System.Windows.Media.TranslateTransform;
                        if (buttonTransform == null)
                        {
                            buttonTransform = new System.Windows.Media.TranslateTransform();
                            buttonGroups.RenderTransform = buttonTransform;
                        }
                        buttonTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, buttonSlide);
                    }

                    // Animate control panel (slide in from right)
                    var controlPanel = this.FindName("ControlPanel") as System.Windows.FrameworkElement;
                    if (controlPanel != null)
                    {
                        // Ensure control panel starts invisible and positioned
                        controlPanel.Opacity = 0.0;
                        var controlTransform = controlPanel.RenderTransform as System.Windows.Media.TranslateTransform;
                        if (controlTransform == null)
                        {
                            controlTransform = new System.Windows.Media.TranslateTransform();
                            controlTransform.X = 20; // Start position (slide from right)
                            controlPanel.RenderTransform = controlTransform;
                        }
                        else
                        {
                            controlTransform.X = 20; // Reset start position
                        }

                        var controlFade = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.7),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        var controlSlide = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 20,
                            To = 0,
                            Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.6)),
                            BeginTime = TimeSpan.FromSeconds(0.7),
                            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                        };

                        controlPanel.BeginAnimation(System.Windows.UIElement.OpacityProperty, controlFade);
                        controlTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, controlSlide);
                    }
                    }
                    catch (Exception innerEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in StartBorderAnimation inner: {innerEx.Message}\n{innerEx.StackTrace}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting border animation: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StartContinuousBorderAnimation(System.Windows.FrameworkElement border)
        {
            try
            {
                if (border == null)
                {
                    System.Diagnostics.Debug.WriteLine("Border is null, cannot start continuous animation");
                    return;
                }

                // Find the leon light rectangle (separate from the dark border)
                var borderElement = border as System.Windows.Controls.Border;
                System.Windows.Shapes.Rectangle? leonLightPath = null;
                
                if (borderElement != null)
                {
                    // Search in the visual tree for the LeonLightPath
                    leonLightPath = FindVisualChild<System.Windows.Shapes.Rectangle>(borderElement, "LeonLightPath");
                }
                
                if (leonLightPath == null)
                {
                    // Try finding by name
                    leonLightPath = border.FindName("LeonLightPath") as System.Windows.Shapes.Rectangle;
                }

                if (leonLightPath == null)
                {
                    System.Diagnostics.Debug.WriteLine("LeonLightPath rectangle not found");
                    return;
                }

                // Create a simple rotating gradient brush with a single bright neon section
                // The base border is solid, and a beautiful neon light rotates smoothly around it
                var gradientBrush = new System.Windows.Media.LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1),
                    MappingMode = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox
                };

                // Continuous visible neon gradient - no fully transparent sections
                // The gradient has a bright section that's always visible as it rotates
                // All sections have at least medium visibility to prevent disappearing
                
                // Start with visible medium brightness (ensures always visible)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.0));
                
                // Gradual fade in to bright neon (smooth transition)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.15));
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(180, 90, 205, 185), 0.25));
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(210, 105, 215, 195), 0.35));
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(240, 115, 218, 198), 0.45));
                
                // Brightest neon center (peak brightness)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.50));
                
                // Gradual fade out from neon (smooth transition)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(240, 115, 218, 198), 0.55));
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(210, 105, 215, 195), 0.65));
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(180, 90, 205, 185), 0.75));
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.85));
                
                // End with visible medium brightness (connects smoothly to start)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 1.0));

                // Create a RotateTransform for the gradient
                var rotateTransform = new System.Windows.Media.RotateTransform(0);
                gradientBrush.RelativeTransform = rotateTransform;

                // Set the gradient brush as the stroke brush for the leon light path
                leonLightPath.Stroke = gradientBrush;
                // Animate the rotation of the gradient slowly and smoothly (360 degrees continuously)
                var rotationAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = new System.Windows.Duration(TimeSpan.FromSeconds(10.0)), // Slower rotation
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };

                rotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotationAnimation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting continuous border animation: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private System.Windows.Media.Animation.DoubleAnimation? _zoomInXAnimation;
        private System.Windows.Media.Animation.DoubleAnimation? _zoomInYAnimation;

        private void StartAnswerButtonNeonLightAnimation()
        {
            try
            {
                var neonLight = this.FindName("AnswerButtonNeonLight") as System.Windows.Shapes.Rectangle;
                if (neonLight == null)
                {
                    System.Diagnostics.Debug.WriteLine("AnswerButtonNeonLight not found");
                    return;
                }

                // Create a gradient brush with a small bright neon segment
                var gradientBrush = new System.Windows.Media.LinearGradientBrush();
                gradientBrush.StartPoint = new System.Windows.Point(0, 0);
                gradientBrush.EndPoint = new System.Windows.Point(1, 0);
                gradientBrush.MappingMode = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox;
                gradientBrush.SpreadMethod = System.Windows.Media.GradientSpreadMethod.Repeat;

                // Most of the gradient is transparent
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.0)); // Transparent at start
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.35)); // Transparent before first light
                
                // First small bright neon segment (about 8% of the gradient)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.37)); // Fade in
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.40)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.42)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.45)); // Fade out
                
                // Transparent between lights
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.47)); // Transparent after first light
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.85)); // Transparent before second light
                
                // Second small bright neon segment (about 8% of the gradient)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.87)); // Fade in
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.90)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.92)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.95)); // Fade out
                
                // Rest is transparent
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.97)); // Transparent after second light
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 1.0)); // Transparent at end

                // Create a RotateTransform for the gradient (rotates around center)
                var rotateTransform = new System.Windows.Media.RotateTransform(0);
                rotateTransform.CenterX = 0.5;
                rotateTransform.CenterY = 0.5;
                gradientBrush.RelativeTransform = rotateTransform;

                // Set the gradient brush as the stroke brush
                neonLight.Stroke = gradientBrush;

                // Create continuous rotation animation (0 to 360 degrees, repeating forever)
                var rotationAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = new System.Windows.Duration(TimeSpan.FromSeconds(3.0)), // 3 seconds per rotation
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };

                rotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotationAnimation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting Answer button neon light animation: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StartBorderNeonLightAnimation()
        {
            try
            {
                var neonLight = this.FindName("BorderNeonLight") as System.Windows.Shapes.Rectangle;
                if (neonLight == null)
                {
                    System.Diagnostics.Debug.WriteLine("BorderNeonLight not found");
                    return;
                }

                // Create a gradient brush with small bright neon segments (same as AnswerButton style)
                var gradientBrush = new System.Windows.Media.LinearGradientBrush();
                gradientBrush.StartPoint = new System.Windows.Point(0, 0);
                gradientBrush.EndPoint = new System.Windows.Point(1, 0);
                gradientBrush.MappingMode = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox;
                gradientBrush.SpreadMethod = System.Windows.Media.GradientSpreadMethod.Repeat;

                // Most of the gradient is transparent
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.0)); // Transparent at start
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.24)); // Transparent before first light
                
                // First bright neon segment (24% of the gradient - doubled length) with smooth fade-in gradient
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.26)); // Start fade in
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(80, 78, 201, 176), 0.30)); // Fade in
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.34)); // Fade in more
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(220, 100, 210, 190), 0.38)); // Getting bright
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.42)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.44)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(220, 100, 210, 190), 0.46)); // Fade out start
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.48)); // Fade out
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(80, 78, 201, 176), 0.50)); // Fade out more
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.52)); // End fade out
                
                // Transparent between lights
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.54)); // Transparent after first light
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.74)); // Transparent before second light
                
                // Second bright neon segment (24% of the gradient - doubled length) with smooth fade-in gradient
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.76)); // Start fade in
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(80, 78, 201, 176), 0.80)); // Fade in
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.84)); // Fade in more
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(220, 100, 210, 190), 0.88)); // Getting bright
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.92)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.94)); // Brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(220, 100, 210, 190), 0.96)); // Fade out start
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(150, 78, 201, 176), 0.98)); // Fade out
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(80, 78, 201, 176), 0.99)); // Fade out more
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 1.0)); // End fade out

                // Create a RotateTransform for the gradient (rotates around center)
                var rotateTransform = new System.Windows.Media.RotateTransform(0);
                rotateTransform.CenterX = 0.5;
                rotateTransform.CenterY = 0.5;
                gradientBrush.RelativeTransform = rotateTransform;

                // Set the gradient brush as the stroke brush
                neonLight.Stroke = gradientBrush;

                // Create continuous rotation animation (0 to 360 degrees, repeating forever)
                // 7 seconds per rotation
                var rotationAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = new System.Windows.Duration(TimeSpan.FromSeconds(7.0)), // 7 seconds per rotation
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };

                rotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotationAnimation);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting border neon light animation: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StartStartupAnimation()
        {
            try
            {
                if (StartupLabelContainer == null) return;

                // Ensure StartupLabelContainer is visible and on top
                StartupLabelContainer.Visibility = Visibility.Visible;
                StartupLabelContainer.Opacity = 0; // Start with 0 for fade-in animation
                System.Windows.Controls.Panel.SetZIndex(StartupLabelContainer, 1000);
                
                // Ensure StartupLabel is visible
                var startupLabel = this.FindName("StartupLabel") as System.Windows.Controls.TextBlock;
                if (startupLabel != null)
                {
                    startupLabel.Visibility = Visibility.Visible;
                    startupLabel.Opacity = 1.0; // Label content should be fully opaque (container controls overall visibility)
                }

                // Animate only the BorderPath rectangle from smallest to correct size with opacity fade-in
                var borderPath = this.FindName("BorderPath") as System.Windows.Shapes.Rectangle;
                if (borderPath != null)
                {
                    // Set initial opacity to 0 for fade-in animation
                    borderPath.Opacity = 0.0;

                    var borderPathTransform = borderPath.RenderTransform as System.Windows.Media.ScaleTransform;
                    if (borderPathTransform == null)
                    {
                        borderPathTransform = new System.Windows.Media.ScaleTransform(0.1, 0.1);
                        borderPath.RenderTransform = borderPathTransform;
                    }

                    // Scale animation (2 seconds - same as label display time)
                    var scaleXAnimation = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0.1,
                        To = 1.0,
                        Duration = new System.Windows.Duration(TimeSpan.FromSeconds(2.0)),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };

                    var scaleYAnimation = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0.1,
                        To = 1.0,
                        Duration = new System.Windows.Duration(TimeSpan.FromSeconds(2.0)),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };

                    // Opacity animation (2 seconds - synchronized with scale animation)
                    var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0.0,
                        To = 1.0,
                        Duration = new System.Windows.Duration(TimeSpan.FromSeconds(2.0)),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };

                    borderPathTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnimation);
                    borderPathTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnimation);
                    borderPath.BeginAnimation(System.Windows.UIElement.OpacityProperty, opacityAnimation);
                }

                // Create fade in animation (0.5 seconds)
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.5)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                // Create slide up animation (0.5 seconds)
                var slideUp = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = -10,
                    To = 0,
                    Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.5)),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                // Get the transform
                var transform = StartupLabelContainer.RenderTransform as System.Windows.Media.TranslateTransform;
                if (transform == null)
                {
                    transform = new System.Windows.Media.TranslateTransform();
                    transform.Y = -10; // Start position for slide-up animation
                    StartupLabelContainer.RenderTransform = transform;
                }
                else
                {
                    transform.Y = -10; // Reset to start position
                }

                // Ensure container is visible before starting animation
                StartupLabelContainer.Visibility = Visibility.Visible;
                StartupLabelContainer.Opacity = 0; // Ensure starting opacity
                
                // Force layout update to ensure visibility
                StartupLabelContainer.UpdateLayout();
                
                // Start fade in and slide up immediately with no delay
                fadeIn.BeginTime = TimeSpan.Zero;
                slideUp.BeginTime = TimeSpan.Zero;
                StartupLabelContainer.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeIn);
                transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideUp);

                // After 2 seconds, fade out and slide down
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2.0)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();

                    // Fade out animation (0.5 seconds)
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.0,
                        Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.5)),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                    };

                    // Slide down animation (0.5 seconds)
                    var slideDown = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = -10,
                        Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.5)),
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                    };

                    // When fade out completes, show the content panels
                    fadeOut.Completed += (sender, args) =>
                    {
                        // Hide the startup label
                        if (StartupLabelContainer != null)
                        {
                            StartupLabelContainer.Visibility = Visibility.Hidden;
                        }

                        // Ensure BorderPath is at correct scale
                        var borderPath = this.FindName("BorderPath") as System.Windows.Shapes.Rectangle;
                        if (borderPath != null)
                        {
                            var borderPathTransform = borderPath.RenderTransform as System.Windows.Media.ScaleTransform;
                            if (borderPathTransform != null)
                            {
                                // Stop animations and ensure scale is at 1.0
                                borderPathTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                                borderPathTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                                borderPathTransform.ScaleX = 1.0;
                                borderPathTransform.ScaleY = 1.0;
                            }
                        }

                        // Show the content panels
                        if (ContentGrid != null)
                        {
                            ContentGrid.Visibility = Visibility.Visible;
                        }
                        
                        // Start the border animation (will animate panels)
                        StartBorderAnimation();
                        
                        // Start Answer button neon light animation
                        StartAnswerButtonNeonLightAnimation();
                        
                        // Start border neon light animation
                        StartBorderNeonLightAnimation();
                    };

                    StartupLabelContainer.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOut);
                    transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideDown);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting startup animation: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StealthBrowser_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess && e.WebErrorStatus != Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.Unknown)
            {
                string errorMessage = $"Navigation failed: {e.WebErrorStatus}";
                if (e.WebErrorStatus == Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.HostNameNotResolved)
                {
                    errorMessage = "Could not resolve the hostname. Please check your internet connection and the URL.";
                }
                else if (e.WebErrorStatus == Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.ConnectionAborted)
                {
                    errorMessage = "Connection was aborted. Please try again.";
                }
                
                MessageBox.Show(errorMessage, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StealthBrowser_CoreWebView2InitializationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess && StealthBrowser?.CoreWebView2 != null)
            {
                // Set zoom to 80% using the WebView2 control's ZoomFactor property
                try
                {
                    StealthBrowser.ZoomFactor = 0.8;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting zoom: {ex.Message}");
                }

                // Make WebView2 background transparent
                try
                {
                    // Set DefaultBackgroundColor to transparent on the WebView2 control
                    StealthBrowser.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                    
                    // Also inject CSS to make body background transparent for web pages and apply cursor system
                    StealthBrowser.CoreWebView2.DOMContentLoaded += async (s, args) =>
                    {
                        string script = @"
                            (function() {
                                if (document.body) {
                                    document.body.style.backgroundColor = 'transparent';
                                }
                                if (document.documentElement) {
                                    document.documentElement.style.backgroundColor = 'transparent';
                                }
                            })();
                        ";
                        await StealthBrowser.CoreWebView2.ExecuteScriptAsync(script);
                        // Apply current cursor system
                        await UpdateWebView2Cursor(_currentCursorSystem);
                    };
                    
                    // Also inject cursor CSS on navigation completed
                    StealthBrowser.CoreWebView2.NavigationCompleted += async (s, args) =>
                    {
                        if (args.IsSuccess)
                        {
                            // Update cursor based on current system
                            await UpdateWebView2Cursor(_currentCursorSystem);
                        }
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting WebView2 transparency: {ex.Message}");
                }

                // Get the host window handle from WebView2
                // WebView2's host window is a child of the WPF window
                IntPtr webView2Handle = GetWebView2HostWindowHandle();
                
                if (webView2Handle != IntPtr.Zero)
                {
                    // Apply exclusion based on current state
                    bool shouldExclude = true;
                    if (DataContext is MainViewModel viewModel)
                    {
                        shouldExclude = viewModel.ExcludedFromCapture;
                    }
                    NativeMethods.SetExcludedFromCapture(webView2Handle, shouldExclude);
                }
            }
        }

        private IntPtr GetWebView2HostWindowHandle()
        {
            if (StealthBrowser == null)
                return IntPtr.Zero;

            // Get the HwndSource for the WebView2 control
            var source = PresentationSource.FromVisual(StealthBrowser) as HwndSource;
            if (source != null)
            {
                return source.Handle;
            }

            // Alternative: try to find child window
            IntPtr parentHandle = new WindowInteropHelper(this).Handle;
            if (parentHandle != IntPtr.Zero)
            {
                // WebView2 creates a child window - we need to find it
                // This is a simplified approach - in practice, WebView2's host window
                // might need to be found through EnumChildWindows
                return FindWebView2ChildWindow(parentHandle);
            }

            return IntPtr.Zero;
        }

        private IntPtr FindWebView2ChildWindow(IntPtr parentHandle)
        {
            IntPtr foundHandle = IntPtr.Zero;
            
            // Use EnumChildWindows to find WebView2's host window
            // WebView2 typically creates a window with class name "Chrome_WidgetWin_1"
            EnumChildWindows(parentHandle, (hWnd, lParam) =>
            {
                const int maxClassName = 256;
                System.Text.StringBuilder className = new System.Text.StringBuilder(maxClassName);
                GetClassName(hWnd, className, maxClassName);
                
                if (className.ToString().Contains("Chrome_WidgetWin"))
                {
                    foundHandle = hWnd;
                    return false; // Stop enumeration
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);

            return foundHandle;
        }

        // P/Invoke for finding child windows
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        private void ApplyWebView2Exclusion(bool enabled)
        {
            IntPtr webView2Handle = GetWebView2HostWindowHandle();
            if (webView2Handle != IntPtr.Zero)
            {
                NativeMethods.SetExcludedFromCapture(webView2Handle, enabled);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                switch (id)
                {
                    case HOTKEY_ID_SEND_TO_WEBVIEW:
                        // Use Dispatcher to handle on UI thread and prevent blocking
                        // Call the same action as Answer button
                        Dispatcher.BeginInvoke(new Action(() => 
                        {
                            _ = SendTextWithPrompt("answer this", includeToggleModifiers: true);
                        }));
                        handled = true;
                        break;
                    case HOTKEY_ID_MOVE_LEFT:
                        MoveWindow(-MOVE_STEP, 0);
                        handled = true;
                        break;
                    case HOTKEY_ID_MOVE_RIGHT:
                        MoveWindow(MOVE_STEP, 0);
                        handled = true;
                        break;
                    case HOTKEY_ID_MOVE_UP:
                        MoveWindow(0, -MOVE_STEP);
                        handled = true;
                        break;
                    case HOTKEY_ID_MOVE_DOWN:
                        MoveWindow(0, MOVE_STEP);
                        handled = true;
                        break;
                    case HOTKEY_ID_WIDTH_DECREASE:
                        ResizeWindow(-RESIZE_STEP, 0);
                        handled = true;
                        break;
                    case HOTKEY_ID_WIDTH_INCREASE:
                        ResizeWindow(RESIZE_STEP, 0);
                        handled = true;
                        break;
                    case HOTKEY_ID_HEIGHT_DECREASE:
                        ResizeWindow(0, -RESIZE_STEP);
                        handled = true;
                        break;
                    case HOTKEY_ID_HEIGHT_INCREASE:
                        ResizeWindow(0, RESIZE_STEP);
                        handled = true;
                        break;
                    case HOTKEY_ID_TOGGLE_VISIBLE:
                        ToggleVisibility();
                        handled = true;
                        break;
                    case HOTKEY_ID_EXIT:
                        Application.Current.Shutdown();
                        handled = true;
                        break;
                    case HOTKEY_ID_OPACITY_INCREASE:
                        AdjustOpacity(OPACITY_STEP);
                        handled = true;
                        break;
                    case HOTKEY_ID_OPACITY_DECREASE:
                        AdjustOpacity(-OPACITY_STEP);
                        handled = true;
                        break;
                    case HOTKEY_ID_SCREENSHOT:
                        CaptureWindowToClipboard();
                        handled = true;
                        break;
                }
            }
            else if (msg == NativeMethods.WM_NCHITTEST)
            {
                // Handle window resizing from border edges
                int x = (int)(lParam.ToInt64() & 0xFFFF);
                int y = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                
                var point = this.PointFromScreen(new System.Windows.Point(x, y));
                double borderThickness = 8; // Resize border thickness
                
                // Check if we're near the edges
                if (point.X < borderThickness)
                {
                    if (point.Y < borderThickness)
                        return new IntPtr(NativeMethods.HTTOPLEFT);
                    else if (point.Y > this.ActualHeight - borderThickness)
                        return new IntPtr(NativeMethods.HTBOTTOMLEFT);
                    else
                        return new IntPtr(NativeMethods.HTLEFT);
                }
                else if (point.X > this.ActualWidth - borderThickness)
                {
                    if (point.Y < borderThickness)
                        return new IntPtr(NativeMethods.HTTOPRIGHT);
                    else if (point.Y > this.ActualHeight - borderThickness)
                        return new IntPtr(NativeMethods.HTBOTTOMRIGHT);
                    else
                        return new IntPtr(NativeMethods.HTRIGHT);
                }
                else if (point.Y < borderThickness)
                {
                    return new IntPtr(NativeMethods.HTTOP);
                }
                else if (point.Y > this.ActualHeight - borderThickness)
                {
                    return new IntPtr(NativeMethods.HTBOTTOM);
                }
                
                // Default: let Windows handle it
                handled = false;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Adjusts window opacity by the specified step amount.
        /// </summary>
        private void AdjustOpacity(double step)
        {
            double newOpacity = Math.Max(OPACITY_MIN, Math.Min(OPACITY_MAX, this.Opacity + step));
            this.Opacity = newOpacity;
            System.Diagnostics.Debug.WriteLine($"Opacity adjusted to: {newOpacity:F2}");
        }

        /// <summary>
        /// Captures the entire window to clipboard (like PrintScreen for this window).
        /// Uses PrintWindow with PW_RENDERFULLCONTENT for better compatibility with layered/transparent windows.
        /// </summary>
        private async void CaptureWindowToClipboard()
        {
            // Fullscreen (all monitors) capture
            IntPtr hdcScreen = IntPtr.Zero;
            IntPtr hdcMem = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOldBitmap = IntPtr.Zero;

            try
            {
                // Get virtual desktop bounds (supports multi-monitor)
                int left   = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
                int top    = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
                int width  = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

                if (width <= 0 || height <= 0)
                    return;

                // 1) Get screen DC (desktop)
                hdcScreen = NativeMethods.GetDC(IntPtr.Zero);

                // 2) Create compatible DC/bitmap
                hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
                hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, width, height);
                hOldBitmap = NativeMethods.SelectObject(hdcMem, hBitmap);

                // 3) Copy pixels from the screen
                // Use CAPTUREBLT to also capture layered windows, drop shadows, etc.
                bool ok = NativeMethods.BitBlt(
                    hdcMem, 0, 0, width, height,
                    hdcScreen, left, top,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

                if (!ok)
                {
                    System.Diagnostics.Debug.WriteLine("Full screen BitBlt failed.");
                    return;
                }

                // 4) Put image on clipboard
                using (var bmp = System.Drawing.Image.FromHbitmap(hBitmap))
                using (var managedCopy = new System.Drawing.Bitmap(bmp))
                {
                    System.Windows.Forms.Clipboard.SetImage(managedCopy);
                }

                System.Diagnostics.Debug.WriteLine($"Fullscreen screenshot copied to clipboard ({width}x{height} @ {left},{top})");

                // 5) Ask ChatGPT to paste (optional)
                await SendPasteToChatGPT();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fullscreen capture error: {ex.Message}");
            }
            finally
            {
                // Cleanup GDI handles
                if (hOldBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(hdcMem, hOldBitmap);
                    hOldBitmap = IntPtr.Zero;
                }
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                    hBitmap = IntPtr.Zero;
                }
                if (hdcMem != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(hdcMem);
                    hdcMem = IntPtr.Zero;
                }
                if (hdcScreen != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
                    hdcScreen = IntPtr.Zero;
                }
            }
        }



        /// <summary>
        /// Sends Ctrl+V (paste) to ChatGPT input field in WebView2.
        /// Uses multiple methods to ensure paste works reliably.
        /// </summary>
        private async Task SendPasteToChatGPT()
        {
            if (StealthBrowser?.CoreWebView2 == null)
            {
                System.Diagnostics.Debug.WriteLine("WebView2 not initialized, cannot send paste command");
                return;
            }

            try
            {
                // Small delay to ensure clipboard is ready
                await Task.Delay(100);

                // JavaScript to find input field and send Ctrl+V
                string script = @"
                    (function() {
                        // ChatGPT-specific: Find the <p> element inside #prompt-textarea
                        let promptTextarea = document.getElementById('prompt-textarea');
                        let targetElement = null;
                        
                        if (promptTextarea) {
                            // Find the <p> child element
                            let pElement = promptTextarea.querySelector('p');
                            if (pElement) {
                                targetElement = pElement;
                            } else {
                                // Fallback: use the div itself if no <p> found
                                targetElement = promptTextarea;
                            }
                        }
                        
                        // If not ChatGPT, try other selectors
                        if (!targetElement) {
                            // Try textarea/input field (works for DeepSeek, Perplexity)
                            targetElement = document.querySelector('textarea[data-id=""root""], textarea[placeholder*=""Message""], textarea[placeholder*=""message""], textarea');
                        }
                        
                        if (!targetElement) {
                            // Try contenteditable divs
                            targetElement = document.querySelector('[role=""textbox""], [contenteditable=""true""]');
                        }
                        
                        if (targetElement) {
                            // Focus the element (or its parent if it's a <p>)
                            let focusTarget = targetElement;
                            if (targetElement.tagName === 'P' && targetElement.parentElement) {
                                focusTarget = targetElement.parentElement;
                            }
                            focusTarget.focus();
                            
                            // Wait a bit for focus to settle
                            setTimeout(() => {
                                // Method 1: Try native paste using execCommand (older but reliable)
                                try {
                                    document.execCommand('paste');
                                } catch(e) {}
                                
                                // Method 2: Create and dispatch Ctrl+V keyboard event
                                const keyDownEvent = new KeyboardEvent('keydown', {
                                    key: 'v',
                                    code: 'KeyV',
                                    keyCode: 86,
                                    which: 86,
                                    ctrlKey: true,
                                    metaKey: false,
                                    altKey: false,
                                    shiftKey: false,
                                    bubbles: true,
                                    cancelable: true
                                });
                                
                                targetElement.dispatchEvent(keyDownEvent);
                                
                                // Method 3: Also try keyup
                                const keyUpEvent = new KeyboardEvent('keyup', {
                                    key: 'v',
                                    code: 'KeyV',
                                    keyCode: 86,
                                    which: 86,
                                    ctrlKey: true,
                                    bubbles: true,
                                    cancelable: true
                                });
                                
                                targetElement.dispatchEvent(keyUpEvent);
                                
                                // Method 4: Try paste event with clipboard data
                                if (navigator.clipboard && navigator.clipboard.read) {
                                    navigator.clipboard.read().then(clipboardItems => {
                                        const pasteEvent = new ClipboardEvent('paste', {
                                            bubbles: true,
                                            cancelable: true,
                                            clipboardData: new DataTransfer()
                                        });
                                        
                                        // Try to set clipboard data if available
                                        if (clipboardItems && clipboardItems.length > 0) {
                                            clipboardItems[0].getType('image/png').then(blob => {
                                                pasteEvent.clipboardData.items.add(blob, 'image/png');
                                                targetElement.dispatchEvent(pasteEvent);
                                            }).catch(() => {
                                                targetElement.dispatchEvent(pasteEvent);
                                            });
                                        } else {
                                            targetElement.dispatchEvent(pasteEvent);
                                        }
                                    }).catch(() => {
                                        // Fallback: just dispatch paste event
                                        const pasteEvent = new ClipboardEvent('paste', {
                                            bubbles: true,
                                            cancelable: true
                                        });
                                        targetElement.dispatchEvent(pasteEvent);
                                    });
                                } else {
                                    // Fallback: dispatch paste event without clipboard data
                                    const pasteEvent = new ClipboardEvent('paste', {
                                        bubbles: true,
                                        cancelable: true
                                    });
                                    targetElement.dispatchEvent(pasteEvent);
                                }
                            }, 50);
                            
                            return 'paste command sent';
                        } else {
                            return 'input not found';
                        }
                    })();
                ";

                string result = await StealthBrowser.CoreWebView2.ExecuteScriptAsync(script);
                System.Diagnostics.Debug.WriteLine($"SendPasteToChatGPT result: {result}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending paste to ChatGPT: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends the last part of the caption to WebView as key events.
        /// Gets text after the last separator and types it character by character into the WebView input.
        /// </summary>
        private async void SendLastCaptionToWebView()
        {
            // Prevent concurrent sends with lock
            lock (_sendLock)
            {
                if (_isSending)
                {
                    System.Diagnostics.Debug.WriteLine("Send already in progress, ignoring hotkey");
                    return;
                }
                _isSending = true;
            }

            try
            {
                if (StealthBrowser?.CoreWebView2 == null)
                {
                    System.Diagnostics.Debug.WriteLine("WebView2 not initialized, cannot send text");
                    return;
                }

                if (DataContext is MainViewModel viewModel)
                {
                    // Get the last part of caption (text that hasn't been sent yet)
                    string lastPart = viewModel.GetLastCaptionPart();
                    if (string.IsNullOrWhiteSpace(lastPart))
                    {
                        System.Diagnostics.Debug.WriteLine("No caption text to send");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"Sending text to WebView (length: {lastPart.Length})");

                    // Mark the text as sent IMMEDIATELY to prevent duplicate sends
                    // This must happen before sending to avoid race conditions
                    viewModel.MarkLastCaptionAsSent();
                    
                    // Send text to WebView as key events
                    bool success = await SendTextToWebViewAsKeyEvents(lastPart);
                    
                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"Text sent to WebView successfully (length: {lastPart.Length})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to send text to WebView");
                        // Note: We already marked as sent, so we won't retry to avoid duplicates
                        // This is intentional - if send fails, we move on to avoid sending wrong data
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending text to WebView: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                lock (_sendLock)
                {
                    _isSending = false;
                }
            }
        }

        /// <summary>
        /// Sends text to WebView input field by simulating key events character by character.
        /// Works for ChatGPT, DeepSeek, and Perplexity.
        /// Returns true if successful, false otherwise.
        /// </summary>
        private async Task<bool> SendTextToWebViewAsKeyEvents(string text)
        {
            if (StealthBrowser?.CoreWebView2 == null)
                return false;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                // JavaScript to find input field and type text character by character
                string escapedText = text
                    .Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("\"", "\\\"")
                    .Replace("\r\n", "\\n")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\n");

                string script = $@"
                    (async function() {{
                        // ChatGPT-specific: Find the <p> element inside #prompt-textarea
                        let promptTextarea = document.getElementById('prompt-textarea');
                        let targetElement = null;
                        
                        if (promptTextarea) {{
                            // Find the <p> child element
                            let pElement = promptTextarea.querySelector('p');
                            if (pElement) {{
                                targetElement = pElement;
                            }} else {{
                                // Fallback: use the div itself if no <p> found
                                targetElement = promptTextarea;
                            }}
                        }}
                        
                        // If not ChatGPT, try other selectors
                        if (!targetElement) {{
                            // Try textarea/input field (works for DeepSeek, Perplexity)
                            targetElement = document.querySelector('textarea[data-id=""root""], textarea[placeholder*=""Message""], textarea[placeholder*=""message""], textarea');
                        }}
                        
                        if (!targetElement) {{
                            // Try contenteditable divs
                            targetElement = document.querySelector('[role=""textbox""], [contenteditable=""true""]');
                        }}
                        
                        if (targetElement) {{
                            // Focus the element (or its parent if it's a <p>)
                            let focusTarget = targetElement;
                            if (targetElement.tagName === 'P' && targetElement.parentElement) {{
                                focusTarget = targetElement.parentElement;
                            }}
                            focusTarget.focus();
                            
                            // Wait a bit for focus to settle
                            await new Promise(resolve => setTimeout(resolve, 50));
                            
                            // Get text to input
                            const text = '{escapedText}';
                            
                            // Append text instead of clearing (preserves user input if any)
                            // For ChatGPT <p> elements, we need to append to the existing content
                            if (targetElement.tagName === 'TEXTAREA' || targetElement.tagName === 'INPUT') {{
                                // For textarea/input, append to value
                                targetElement.value += text;
                            }} else {{
                                // For <p> elements or contenteditable, append to textContent
                                targetElement.textContent += text;
                            }}
                            
                            // Trigger input event
                            const inputEvent = new Event('input', {{ bubbles: true, cancelable: true }});
                            targetElement.dispatchEvent(inputEvent);
                            
                            // Also trigger on parent if it's a <p>
                            if (targetElement.tagName === 'P' && targetElement.parentElement) {{
                                targetElement.parentElement.dispatchEvent(inputEvent);
                            }}
                            
                            // Trigger change event
                            const changeEvent = new Event('change', {{ bubbles: true, cancelable: true }});
                            targetElement.dispatchEvent(changeEvent);
                            
                            // Also trigger on parent if it's a <p>
                            if (targetElement.tagName === 'P' && targetElement.parentElement) {{
                                targetElement.parentElement.dispatchEvent(changeEvent);
                            }}
                            
                            // Wait a bit before clicking submit
                            await new Promise(resolve => setTimeout(resolve, 100));
                            
                            // After setting text, click the submit button (ChatGPT)
                            let submitButton = document.getElementById('composer-submit-button');
                            if (submitButton && !submitButton.disabled) {{
                                submitButton.click();
                                return 'success - text appended and button clicked';
                            }} else {{
                                return 'success - text appended, button not found or disabled';
                            }}
                        }} else {{
                            return 'input not found';
                        }}
                    }})();
                ";

                string result = await StealthBrowser.CoreWebView2.ExecuteScriptAsync(script);
                System.Diagnostics.Debug.WriteLine($"SendTextToWebView result: {result}");
                
                // Check if result indicates success
                if (result != null && (result.Contains("success") || result.Contains("text")))
                {
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending text to WebView: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private void AddressBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                NavigateToUrl();
                e.Handled = true;
            }
        }

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUrl();
        }

        private async void NavigateToUrl()
        {
            if (DataContext is MainViewModel viewModel && StealthBrowser != null)
            {
                try
                {
                    // Get the current URL from the view model
                    string url = viewModel.BrowserUrl?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(url))
                        return;

                    // Update the view model with the trimmed URL
                    viewModel.BrowserUrl = url;

                    // Ensure WebView2 is initialized
                    if (StealthBrowser.CoreWebView2 == null)
                    {
                        try
                        {
                            await StealthBrowser.EnsureCoreWebView2Async();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"WebView2 initialization failed. Please ensure Microsoft Edge WebView2 Runtime is installed.\n\nError: {ex.Message}", 
                                "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }

                    // Handle special URLs like about:blank
                    if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (StealthBrowser.CoreWebView2 != null)
                        {
                            StealthBrowser.CoreWebView2.Navigate(url);
                        }
                        else
                        {
                            StealthBrowser.Source = new Uri(url);
                        }
                        return;
                    }

                    // Ensure URL has protocol for regular URLs
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "https://" + url;
                        viewModel.BrowserUrl = url;
                    }

                    // Navigate using CoreWebView2 for better error handling
                    if (StealthBrowser.CoreWebView2 != null)
                    {
                        StealthBrowser.CoreWebView2.Navigate(url);
                    }
                    else
                    {
                        StealthBrowser.Source = new Uri(url);
                    }
                }
                catch (UriFormatException ex)
                {
                    MessageBox.Show($"Invalid URL format: {ex.Message}\n\nPlease enter a valid URL (e.g., google.com or https://example.com)", 
                        "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error navigating to URL: {ex.Message}\n\nPlease check:\n1. You have an internet connection\n2. The URL is correct\n3. WebView2 runtime is installed", 
                        "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Grid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Enable window dragging when clicking on the grid (since we removed title bar)
            // Only drag if not clicking on resize grips
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left && 
                e.OriginalSource is not System.Windows.Shapes.Rectangle)
            {
                try
                {
                    this.DragMove();
                }
                catch
                {
                    // Ignore drag errors to prevent crashes
                }
            }
        }

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Enable window dragging when clicking on the border
            // Only drag if not clicking on resize grips
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left && 
                e.OriginalSource is not System.Windows.Shapes.Rectangle)
            {
                try
                {
                    this.DragMove();
                }
                catch
                {
                    // Ignore drag errors to prevent crashes
                }
            }
        }


        private void MoveWindow(double deltaX, double deltaY)
        {
            this.Left += deltaX;
            this.Top += deltaY;
            
            // Update help window position if it's visible
            UpdateHelpWindowPosition();
        }
        
        private void UpdateHelpWindowPosition()
        {
            if (_helpWindow != null && _helpWindow.IsVisible)
            {
                // Position to the right of the control panel
                double controlPanelRight = this.Left + this.Width - 10; // Control panel right edge (10px margin from window right)
                _helpWindow.Left = controlPanelRight + 10; // 10px spacing after control panel
                
                // Align with bottom of main panel (10px margin from bottom)
                _helpWindow.Top = this.Top + this.Height - _helpWindow.ActualHeight - 10;
            }
        }

        private void ResizeWindow(double deltaWidth, double deltaHeight)
        {
            this.Width = Math.Max(300, this.Width + deltaWidth);
            this.Height = Math.Max(200, this.Height + deltaHeight);
            
            // Update help window position if it's visible
            UpdateHelpWindowPosition();
        }
        
        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            // Update help window position when main window moves
            UpdateHelpWindowPosition();
        }
        
        private void Window_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // Update help window position when main window is resized
            UpdateHelpWindowPosition();
        }

        private void ToggleVisibility()
        {
            if (this.Visibility == Visibility.Visible)
            {
                this.Visibility = Visibility.Hidden;
            }
            else
            {
                this.Visibility = Visibility.Visible;
                this.Activate();
            }
        }

        private bool _autoScrollToBottom = true;
        private void CaptionScrollViewer_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            // Track if user manually scrolled up
            if (e.VerticalChange > 0 && e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 10)
            {
                // User scrolled up manually, disable auto-scroll
                _autoScrollToBottom = false;
            }
            // Re-enable auto-scroll if user scrolls to bottom
            else if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 10)
            {
                _autoScrollToBottom = true;
            }
        }

        private void ScrollToBottom()
        {
            if (CaptionScrollViewer != null && _autoScrollToBottom)
            {
                try
                {
                    // Update layout first to ensure content is measured
                    CaptionScrollViewer.UpdateLayout();
                    
                    // Scroll to end - this will scroll to the very bottom
                    CaptionScrollViewer.ScrollToEnd();
                    
                    // Also ensure vertical offset is at maximum
                    if (CaptionScrollViewer.ScrollableHeight > 0)
                    {
                        // Use ExtentHeight to scroll to the absolute bottom
                        double maxOffset = CaptionScrollViewer.ExtentHeight - CaptionScrollViewer.ViewportHeight;
                        if (maxOffset > 0)
                        {
                            CaptionScrollViewer.ScrollToVerticalOffset(maxOffset);
                        }
                        else
                        {
                            CaptionScrollViewer.ScrollToVerticalOffset(CaptionScrollViewer.ExtentHeight);
                        }
                    }
                }
                catch { /* Ignore scroll errors */ }
            }
        }

        private void ChatGPTButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.BrowserUrl = "https://chatgpt.com";
                NavigateToUrl();
            }
        }

        private void DeepSeekButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.BrowserUrl = "https://chat.deepseek.com";
                NavigateToUrl();
            }
        }

        private void PerplexityButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.BrowserUrl = "https://www.perplexity.ai";
                NavigateToUrl();
            }
        }

        private void GrokButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.BrowserUrl = "https://x.ai/grok";
                NavigateToUrl();
            }
        }

        private void ControlPanelChatGPTButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.BrowserUrl = "https://chatgpt.com";
                NavigateToUrl();
            }
        }

        private void CaptionTextSizeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (CaptionTextBlock != null && CaptionTextSizeLabel != null)
            {
                double fontSize = e.NewValue;
                
                // Update text font size
                CaptionTextBlock.FontSize = fontSize;
                
                // Update label
                CaptionTextSizeLabel.Text = $"{(int)fontSize}";
            }
        }

        private IntPtr FindLiveCaptionsHwnd()
        {
            // Try UIA first
            try
            {
                var root = System.Windows.Automation.AutomationElement.RootElement;
                var cond = new System.Windows.Automation.AndCondition(
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.ControlTypeProperty,
                        System.Windows.Automation.ControlType.Window),
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.IsWindowPatternAvailableProperty, true)
                );

                var windows = root.FindAll(System.Windows.Automation.TreeScope.Children, cond);
                foreach (System.Windows.Automation.AutomationElement w in windows)
                {
                    string name = w.Current.Name ?? "";
                    if (name.IndexOf("Live captions", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int handle = w.Current.NativeWindowHandle;
                        if (handle != 0) return new IntPtr(handle);
                    }
                }
            }
            catch { /* ignore */ }

            // Fallback: EnumWindows by title/class
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows((h, l) =>
            {
                var title = new StringBuilder(256);
                NativeMethods.GetWindowText(h, title, title.Capacity);
                string t = title.ToString();

                if (!string.IsNullOrWhiteSpace(t) &&
                    t.IndexOf("Live captions", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = h;
                    return false;
                }

                // UWP host sometimes is ApplicationFrameWindow; peek children
                var cls = new StringBuilder(256);
                NativeMethods.GetClassName(h, cls, cls.Capacity);
                if (cls.ToString().Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
                {
                    IntPtr child = NativeMethods.FindWindowEx(h, IntPtr.Zero, null, null);
                    while (child != IntPtr.Zero)
                    {
                        var ctitle = new StringBuilder(256);
                        NativeMethods.GetWindowText(child, ctitle, ctitle.Capacity);
                        if (ctitle.ToString().IndexOf("Live captions", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = h;
                            return false;
                        }
                        child = NativeMethods.FindWindowEx(h, child, null, null);
                    }
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        private bool TryCloakWindow(IntPtr hwnd)
        {
            try
            {
                int cloak = 1; // TRUE
                int hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAK, ref cloak, sizeof(int));
                return hr == 0;
            }
            catch { return false; }
        }

        private bool TryUncloakWindow(IntPtr hwnd)
        {
            try
            {
                int cloak = 0; // FALSE
                int hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAK, ref cloak, sizeof(int));
                return hr == 0;
            }
            catch { return false; }
        }

        private bool IsCloaked(IntPtr hwnd)
        {
            try
            {
                int val;
                int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out val, sizeof(int));
                return (hr == 0) && (val != 0);
            }
            catch { return false; }
        }



        private void ToggleSystemCaptionsButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle hide/show the Live Captions window with cloaking
            if (LiveCaptionsTaskbarHider.IsHidden())
            {
                // Window is hidden, show it (uncloak and restore position)
                LiveCaptionsTaskbarHider.Show();
            }
            else
            {
                // Window is visible, hide it (cloak and move off-screen)
                LiveCaptionsTaskbarHider.HideKeepRunning();
            }
        }

        // Toggle button handlers (no-op, just for visual feedback)
        private void ToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            // Toggle button state is handled by the control itself
        }

        private void ToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            // Toggle button state is handled by the control itself
        }

        // Get list of active toggle button values
        private List<string> GetActiveToggleValues()
        {
            var activeValues = new List<string>();
            
            if (ToggleProfessional?.IsChecked == true) activeValues.Add("Professional");
            if (ToggleInteresting?.IsChecked == true) activeValues.Add("Interesting - light Funny");
            if (ToggleSTAR?.IsChecked == true) activeValues.Add("STAR");
            if (ToggleSharp?.IsChecked == true) activeValues.Add("Sharp");
            if (ToggleCreative?.IsChecked == true) activeValues.Add("Creative");
            if (ToggleMentioning?.IsChecked == true) activeValues.Add("Mentioning specific thing(s)");
            if (ToggleStoryMode?.IsChecked == true) activeValues.Add("Story mode");
            if (ToggleImpactful?.IsChecked == true) activeValues.Add("Impactful");
            if (ToggleTechStack?.IsChecked == true) activeValues.Add("mentioning Tech stack versions at the next of each tech stacks inside the answer if the answer explains technical problem");
            if (ToggleDetailed?.IsChecked == true) activeValues.Add("Detailed");
            if (ToggleStepByStep?.IsChecked == true) activeValues.Add("Step-by-Step");
            
            return activeValues;
        }

        // Get selected radio button value for duration
        private string? GetSelectedDurationValue()
        {
            if (Radio1Min?.IsChecked == true) return "for 1 min";
            if (Radio1To2Mins?.IsChecked == true) return "for 1-2mins";
            if (Radio2To3Mins?.IsChecked == true) return "for 2-3mins";
            return null;
        }

        // Send text with prompt wrapper
        private async Task SendTextWithPrompt(string prompt, bool includeToggleModifiers = false)
        {
            // Prevent concurrent sends with lock
            lock (_sendLock)
            {
                if (_isSending)
                {
                    System.Diagnostics.Debug.WriteLine("Send already in progress, ignoring button click");
                    return;
                }
                _isSending = true;
            }

            try
            {
                if (StealthBrowser?.CoreWebView2 == null)
                {
                    System.Diagnostics.Debug.WriteLine("WebView2 not initialized, cannot send text");
                    return;
                }

                if (DataContext is MainViewModel viewModel)
                {
                    // Get the last part of caption (text that hasn't been sent yet)
                    string lastPart = viewModel.GetLastCaptionPart();
                    if (string.IsNullOrWhiteSpace(lastPart))
                    {
                        System.Diagnostics.Debug.WriteLine("No caption text to send");
                        return;
                    }

                    // Build the prompt
                    string finalPrompt = prompt;
                    
                    if (includeToggleModifiers)
                    {
                        var toggleValues = GetActiveToggleValues();
                        var durationValue = GetSelectedDurationValue();
                        
                        if (toggleValues.Count > 0 || durationValue != null)
                        {
                            var allValues = new List<string>(toggleValues);
                            if (durationValue != null)
                            {
                                allValues.Add(durationValue);
                            }
                            string toggleString = string.Join(", ", allValues);
                            // Replace "answer this" with "Answer this with {toggles} answer"
                            finalPrompt = $"Answer for this interviewer's speech. [Tone & Length]: {toggleString}.";
                        }
                    }

                    // Wrap the original data with quotes and add prompt
                    string wrappedText = $"\"{lastPart}\" {finalPrompt}";

                    System.Diagnostics.Debug.WriteLine($"Sending text to WebView with prompt (length: {wrappedText.Length})");

                    // Mark the text as sent IMMEDIATELY to prevent duplicate sends
                    viewModel.MarkLastCaptionAsSent();
                    
                    // Send text to WebView
                    bool success = await SendTextToWebViewAsKeyEvents(wrappedText);
                    
                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"Text sent to WebView successfully (length: {wrappedText.Length})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to send text to WebView");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending text to WebView: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                lock (_sendLock)
                {
                    _isSending = false;
                }
            }
        }

        private async void AnswerButton_Click(object sender, RoutedEventArgs e)
        {
            await SendTextWithPrompt("answer this", includeToggleModifiers: true);
        }

        private async void FollowUpButton_Click(object sender, RoutedEventArgs e)
        {
            await SendTextWithPrompt("how to followup this and give me questions if the question is needed to give impact");
        }

        private async void ClarifyButton_Click(object sender, RoutedEventArgs e)
        {
            await SendTextWithPrompt("give me clarification for this. give me clear vision of this with bullets and some questions that I can ask if something unclear here");
        }



        private void HideFromAltTab()
        {
            if (_helper != null && _helper.Handle != IntPtr.Zero)
            {
                int exStyle = NativeMethods.GetWindowLong(_helper.Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
                NativeMethods.SetWindowLong(_helper.Handle, NativeMethods.GWL_EXSTYLE, exStyle);
            }
        }

        private void SwitchCursorSystem(CursorSystem system)
        {
            // Check if system is actually changing
            bool systemChanged = system != _currentCursorSystem;
            
            _currentCursorSystem = system;
            System.Windows.Input.Cursor cursor = null;
            
            if (system == CursorSystem.Arrow)
            {
                cursor = System.Windows.Input.Cursors.Arrow;
            }
            else if (system == CursorSystem.Caret)
            {
                cursor = System.Windows.Input.Cursors.IBeam;
            }
            // Normal system: cursor is null, which restores original cursors

            // Skip recursive update only if:
            // 1. System hasn't changed AND
            // 2. Both cursors are non-null AND equal
            // This ensures we always update on startup (when both are null) or when switching systems
            if (!systemChanged && cursor != null && _lastAppliedCursor != null && cursor == _lastAppliedCursor)
            {
                // Only skip if we're not changing systems and cursors are the same
                // But still update button states in case they're not set correctly
                UpdateButtonStates(system);
                return;
            }
            
            _lastAppliedCursor = cursor;
            _previousCursorSystem = system;
            
            // Clear cache when switching cursor systems
            _cursorCache.Clear();

            // Update window cursor (this will cascade to all child elements unless overridden)
            this.Cursor = cursor;

            // Recursively update all UI elements to ensure cursor is applied everywhere
            // For Normal system, pass null to restore original cursors
            UpdateCursorRecursive(this, cursor);
            
            // Special handling: CaretCursorButton should always show Caret cursor on hover
            if (CaretCursorButton != null)
            {
                // Don't override CaretCursorButton cursor here - it's handled by MouseEnter/MouseLeave
            }

            // Update button states
            UpdateButtonStates(system);

            // Update WebView2 cursor via JavaScript
            UpdateWebView2Cursor(system);
        }

        private void UpdateButtonStates(CursorSystem system)
        {
            // Update button states
            if (ArrowCursorButton != null)
            {
                if (system == CursorSystem.Arrow)
                {
                    var activeColor = System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0);
                    ArrowCursorButton.Background = new System.Windows.Media.SolidColorBrush(activeColor);
                    ArrowCursorButton.BorderBrush = new System.Windows.Media.SolidColorBrush(activeColor);
                }
                else
                {
                    var inactiveColor = System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30);
                    var defaultBorderColor = System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46);
                    ArrowCursorButton.Background = new System.Windows.Media.SolidColorBrush(inactiveColor);
                    ArrowCursorButton.BorderBrush = new System.Windows.Media.SolidColorBrush(defaultBorderColor);
                }
            }

            if (CaretCursorButton != null)
            {
                if (system == CursorSystem.Caret)
                {
                    var activeColor = System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0);
                    CaretCursorButton.Background = new System.Windows.Media.SolidColorBrush(activeColor);
                    CaretCursorButton.BorderBrush = new System.Windows.Media.SolidColorBrush(activeColor);
                }
                else
                {
                    var inactiveColor = System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30);
                    var defaultBorderColor = System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46);
                    CaretCursorButton.Background = new System.Windows.Media.SolidColorBrush(inactiveColor);
                    CaretCursorButton.BorderBrush = new System.Windows.Media.SolidColorBrush(defaultBorderColor);
                }
            }

            if (NormalCursorButton != null)
            {
                if (system == CursorSystem.Normal)
                {
                    var activeColor = System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0);
                    NormalCursorButton.Background = new System.Windows.Media.SolidColorBrush(activeColor);
                    NormalCursorButton.BorderBrush = new System.Windows.Media.SolidColorBrush(activeColor);
                }
                else
                {
                    var inactiveColor = System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30);
                    var defaultBorderColor = System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46);
                    NormalCursorButton.Background = new System.Windows.Media.SolidColorBrush(inactiveColor);
                    NormalCursorButton.BorderBrush = new System.Windows.Media.SolidColorBrush(defaultBorderColor);
                }
            }
        }

        private async Task UpdateWebView2Cursor(CursorSystem system)
        {
            if (StealthBrowser?.CoreWebView2 == null)
                return;

            try
            {
                // For Normal system, remove the cursor override style
                if (system == CursorSystem.Normal)
                {
                    string removeScript = @"
                        (function() {
                            var existingStyle = document.getElementById('cursor-system-style');
                            if (existingStyle) {
                                existingStyle.remove();
                            }
                        })();
                    ";
                    await StealthBrowser.CoreWebView2.ExecuteScriptAsync(removeScript);
                    return;
                }

                string cursorValue = system == CursorSystem.Arrow ? "default" : "text";
                string script = $@"
                    (function() {{
                        var style = document.createElement('style');
                        style.textContent = '* {{ cursor: {cursorValue} !important; }}';
                        if (document.head) {{
                            // Remove existing cursor style if any
                            var existingStyle = document.getElementById('cursor-system-style');
                            if (existingStyle) {{
                                existingStyle.remove();
                            }}
                            style.id = 'cursor-system-style';
                            document.head.appendChild(style);
                        }} else {{
                            document.addEventListener('DOMContentLoaded', function() {{
                                var existingStyle = document.getElementById('cursor-system-style');
                                if (existingStyle) {{
                                    existingStyle.remove();
                                }}
                                style.id = 'cursor-system-style';
                                document.head.appendChild(style);
                            }});
                        }}
                    }})();
                ";
                await StealthBrowser.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating WebView2 cursor: {ex.Message}");
            }
        }

        private void ArrowCursorButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchCursorSystem(CursorSystem.Arrow);
        }

        private void CaretCursorButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchCursorSystem(CursorSystem.Caret);
        }

        private void NormalCursorButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchCursorSystem(CursorSystem.Normal);
        }

        private void ModeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                // Toggle the mode
                viewModel.IsInterviewMode = !viewModel.IsInterviewMode;
                
                // Update button text
                if (ModeToggleButtonText != null)
                {
                    ModeToggleButtonText.Text = viewModel.IsInterviewMode ? "I" : "N";
                }
                
                // Show/hide LeftPanel (live caption panel) and GridSplitter based on mode
                if (LeftPanel != null)
                {
                    if (viewModel.IsInterviewMode)
                    {
                        // Interview Mode: Show LeftPanel
                        LeftPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Normal Mode: Hide LeftPanel
                        LeftPanel.Visibility = Visibility.Collapsed;
                    }
                }
                
                // Also hide/show the GridSplitter
                if (MainGridSplitter != null)
                {
                    if (viewModel.IsInterviewMode)
                    {
                        // Interview Mode: Show GridSplitter
                        MainGridSplitter.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Normal Mode: Hide GridSplitter
                        MainGridSplitter.Visibility = Visibility.Collapsed;
                    }
                }
                
                // Also hide/show the SplitterBorder (visual splitter)
                var splitterBorder = this.FindName("SplitterBorder") as System.Windows.FrameworkElement;
                if (splitterBorder != null)
                {
                    if (viewModel.IsInterviewMode)
                    {
                        // Interview Mode: Show SplitterBorder
                        splitterBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Normal Mode: Hide SplitterBorder
                        splitterBorder.Visibility = Visibility.Collapsed;
                    }
                }
                
                // Adjust column widths based on mode
                var captionColumn = this.FindName("CaptionColumn") as System.Windows.Controls.ColumnDefinition;
                if (captionColumn != null)
                {
                    // Get the parent grid to access the splitter column
                    var parentGrid = captionColumn.Parent as System.Windows.Controls.Grid;
                    if (parentGrid != null && parentGrid.ColumnDefinitions.Count > 2)
                    {
                        var splitterColumn = parentGrid.ColumnDefinitions[1]; // Splitter column is at index 1
                        var rightColumn = parentGrid.ColumnDefinitions[2]; // Right column (WebView) is at index 2
                        
                        if (viewModel.IsInterviewMode)
                        {
                            // Interview Mode: Restore original column widths and MinWidth constraints
                            captionColumn.Width = new System.Windows.GridLength(5, System.Windows.GridUnitType.Star);
                            captionColumn.MinWidth = 100;
                            splitterColumn.Width = new System.Windows.GridLength(10);
                            splitterColumn.MinWidth = 10;
                            rightColumn.MinWidth = 100;
                        }
                        else
                        {
                            // Normal Mode: Collapse both left column and splitter column so WebView takes full width
                            // Also set MinWidth to 0 to override the XAML MinWidth constraints
                            captionColumn.Width = new System.Windows.GridLength(0);
                            captionColumn.MinWidth = 0;
                            splitterColumn.Width = new System.Windows.GridLength(0);
                            splitterColumn.MinWidth = 0;
                            rightColumn.MinWidth = 0; // Allow right column to expand fully
                        }
                    }
                }
                
                // Show/hide button groups container based on mode
                if (ButtonGroupsContainer != null)
                {
                    if (viewModel.IsInterviewMode)
                    {
                        // Interview Mode: Show button groups
                        ButtonGroupsContainer.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Normal Mode: Hide button groups
                        ButtonGroupsContainer.Visibility = Visibility.Collapsed;
                    }
                }
                
                // Adjust RightPanel margin based on mode to remove gap
                if (RightPanel != null)
                {
                    if (viewModel.IsInterviewMode)
                    {
                        // Interview Mode: Restore original margin (5, 10, 8, 10)
                        RightPanel.Margin = new System.Windows.Thickness(5, 10, 8, 10);
                    }
                    else
                    {
                        // Normal Mode: Remove left margin to eliminate gap (10, 10, 8, 10)
                        RightPanel.Margin = new System.Windows.Thickness(10, 10, 8, 10);
                    }
                }
                
                // Set window width based on mode
                if (viewModel.IsInterviewMode)
                {
                    // Interview Mode: Set width to 680
                    this.Width = 680;
                }
                else
                {
                    // Normal Mode: Set width to 340
                    this.Width = 400;
                }
                
                // Update help window position after width change
                UpdateHelpWindowPosition();
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle independent help window
            if (_helpWindow == null || !_helpWindow.IsVisible)
            {
                // Create or show help window
                if (_helpWindow == null)
                {
                    _helpWindow = new HelpWindow();
                    _helpWindow.Owner = this;
                }
                
                // Position to the right of the control panel
                // Control panel is 55px wide with 10px right margin
                // Position help window to the right of control panel with 10px spacing
                double controlPanelRight = this.Left + this.Width - 10; // Control panel right edge (10px margin from window right)
                _helpWindow.Left = controlPanelRight + 10; // 10px spacing after control panel
                
                // Show window first to get its actual height (since SizeToContent="Height")
                _helpWindow.Show();
                _helpWindow.UpdateLayout(); // Ensure layout is updated
                
                // Align with bottom of main panel (10px margin from bottom)
                _helpWindow.Top = this.Top + this.Height - _helpWindow.ActualHeight - 10;
                
                // Start fade-in animation
                _helpWindow.StartFadeIn();
                
                // Update ViewModel to reflect help window is visible
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.IsHelpWindowVisible = true;
                }
            }
            else
            {
                // Start fade-out animation and hide
                _helpWindow.StartFadeOut(() =>
                {
                    _helpWindow.Hide();
                    
                    // Update ViewModel to reflect help window is hidden
                    if (DataContext is MainViewModel viewModel)
                    {
                        viewModel.IsHelpWindowVisible = false;
                    }
                });
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            // Exit the application (not hide) - use graceful shutdown
            PerformGracefulShutdown();
        }

        private async void PerformGracefulShutdown()
        {
            try
            {
                // Stop all timers immediately to prevent any further operations
                if (_moveTimer != null)
                {
                    _moveTimer.Stop();
                    _moveTimer.Tick -= MoveTimer_Tick;
                }

                if (_resizeTimer != null)
                {
                    _resizeTimer.Stop();
                    _resizeTimer.Tick -= ResizeTimer_Tick;
                }

                // Close WebView2 explicitly - this is critical for long-running sessions
                if (StealthBrowser != null)
                {
                    try
                    {
                        // Remove event handlers before closing to prevent issues
                        StealthBrowser.CoreWebView2InitializationCompleted -= StealthBrowser_CoreWebView2InitializationCompleted;
                        StealthBrowser.NavigationCompleted -= StealthBrowser_NavigationCompleted;

                        // Dispose the WebView2 control to release all resources
                        // This will automatically close CoreWebView2 and clean up all browser processes
                        StealthBrowser.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error closing WebView2: {ex.Message}");
                        // Continue with shutdown even if WebView2 cleanup fails
                    }
                }

                // Give WebView2 a brief moment to clean up its processes
                // This prevents crashes when exiting after long runtime
                await Task.Delay(100);

                // Now shutdown the application
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during graceful shutdown: {ex.Message}");
                // Force shutdown even if there's an error
                Application.Current.Shutdown();
            }
        }

        private void CaretCursorButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Always show Caret cursor when hovering over CaretCursorButton
            if (CaretCursorButton != null)
            {
                CaretCursorButton.Cursor = System.Windows.Input.Cursors.IBeam;
            }
        }

        private void CaretCursorButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Restore to current cursor system when mouse leaves
            if (CaretCursorButton != null)
            {
                System.Windows.Input.Cursor cursor = _currentCursorSystem == CursorSystem.Arrow 
                    ? System.Windows.Input.Cursors.Arrow 
                    : System.Windows.Input.Cursors.IBeam;
                CaretCursorButton.Cursor = cursor;
            }
        }

        private void UpdateCursorRecursive(System.Windows.DependencyObject element, System.Windows.Input.Cursor cursor)
        {
            if (element == null) return;

            // Update cursor for FrameworkElement
            if (element is System.Windows.FrameworkElement fe)
            {
                // Skip CaretCursorButton - it has special handling
                if (fe.Name != "CaretCursorButton")
                {
                    System.Windows.Input.Cursor cursorToApply;
                    
                    // Special handling for buttons in Normal mode: restore default Hand cursor
                    if (cursor == null && fe is System.Windows.Controls.Primitives.ButtonBase)
                    {
                        // In Normal mode, buttons should show Hand cursor (default Windows behavior)
                        cursorToApply = System.Windows.Input.Cursors.Hand;
                    }
                    else
                    {
                        cursorToApply = cursor;
                    }
                    
                    // Always update if:
                    // 1. Element is not in cache, OR
                    // 2. Cached cursor doesn't match what we want to apply, OR
                    // 3. Element's current cursor doesn't match what we want to apply
                    // This ensures we fix any XAML-set cursors that don't match the cursor system
                    bool needsUpdate = !_cursorCache.ContainsKey(element) 
                        || _cursorCache[element] != cursorToApply
                        || fe.Cursor != cursorToApply;
                    
                    if (needsUpdate)
                    {
                        fe.Cursor = cursorToApply;
                        _cursorCache[element] = cursorToApply;
                    }
                }
            }

            // Recursively update all children (limit depth to prevent excessive recursion)
            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(element, i);
                UpdateCursorRecursive(child, cursor);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Dispose ViewModel to clean up event handlers and resources
            if (DataContext is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }

            // Unregister all hotkeys
            if (_helper != null && _helper.Handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_SEND_TO_WEBVIEW);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_LEFT);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_RIGHT);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_UP);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_DOWN);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_WIDTH_DECREASE);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_WIDTH_INCREASE);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_HEIGHT_DECREASE);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_HEIGHT_INCREASE);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_TOGGLE_VISIBLE);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_EXIT);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_OPACITY_INCREASE);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_OPACITY_DECREASE);
                NativeMethods.UnregisterHotKey(_helper.Handle, HOTKEY_ID_SCREENSHOT);
            }

            // Remove keyboard hook (CRITICAL: Prevents memory leak and conflicts)
            if (_keyboardHook != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                    System.Diagnostics.Debug.WriteLine("Keyboard hook uninstalled successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error uninstalling keyboard hook: {ex.Message}");
                }
                finally
                {
                    _keyboardHook = IntPtr.Zero;
                }
            }

            // Remove WndProc hook
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            // Stop and dispose timers
            if (_moveTimer != null)
            {
                _moveTimer.Stop();
                _moveTimer.Tick -= MoveTimer_Tick;
                _moveTimer = null;
            }

            if (_resizeTimer != null)
            {
                _resizeTimer.Stop();
                _resizeTimer.Tick -= ResizeTimer_Tick;
                _resizeTimer = null;
            }

            // Clean up WebView2 as backup safety measure
            // (Primary cleanup should happen in PerformGracefulShutdown, but this ensures cleanup even if shutdown is called directly)
            if (StealthBrowser != null)
            {
                try
                {
                    // Remove event handlers before closing
                    StealthBrowser.CoreWebView2InitializationCompleted -= StealthBrowser_CoreWebView2InitializationCompleted;
                    StealthBrowser.NavigationCompleted -= StealthBrowser_NavigationCompleted;

                    // Dispose the WebView2 control to release all resources
                    // This will automatically close CoreWebView2 and clean up all browser processes
                    StealthBrowser.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cleaning up WebView2 in OnClosed: {ex.Message}");
                    // Continue with cleanup even if WebView2 disposal fails
                }
            }

            // Close help window if it exists
            if (_helpWindow != null)
            {
                try
                {
                    _helpWindow.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error closing help window: {ex.Message}");
                }
            }

            base.OnClosed(e);
        }
    }
}
