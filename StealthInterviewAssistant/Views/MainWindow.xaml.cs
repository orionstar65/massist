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
        private const int MOVE_STEP = 8; // pixels to move per tick (increased for faster movement)
        private const int RESIZE_STEP = 10; // pixels to resize per tick (increased for faster resizing)
        private const int TIMER_INTERVAL_MS = 10; // ~100fps for faster, smoother movement
        
        // Lock to prevent concurrent hotkey sends
        private readonly object _sendLock = new object();
        private bool _isSending = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            _helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(_helper.Handle);
            
            if (_hwndSource != null)
            {
                // Add HwndSourceHook for WM_HOTKEY
                _hwndSource.AddHook(WndProc);
            }

            // Register all global hotkeys
            if (_helper != null)
            {
                // Send to WebView: Ctrl+Shift+/
                // '/' key is VK_OEM_2 (0xBF) on US keyboard
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_SEND_TO_WEBVIEW,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    0xBF); // VK_OEM_2 for '/' key

                // Window movement: Ctrl+Shift+J/K/L/I
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_LEFT,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.J);
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_RIGHT,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.L);
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_UP,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.I);
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_MOVE_DOWN,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.K);

                // Window width: Ctrl+Shift+R/T
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_WIDTH_DECREASE,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.R);
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_WIDTH_INCREASE,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.T);

                // Window height: Ctrl+Shift+Q/A
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_HEIGHT_DECREASE,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.Q);
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_HEIGHT_INCREASE,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.A);

                // Toggle visibility: Ctrl+Shift+H
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_TOGGLE_VISIBLE,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.H);

                // Exit: Ctrl+Shift+P
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_EXIT,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.P);

                // Opacity control: Ctrl+Shift+Up/Down Arrow
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_OPACITY_INCREASE,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.Up);
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_OPACITY_DECREASE,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.Down);

                // Screenshot: Ctrl+Shift+.
                NativeMethods.RegisterHotKey(_helper.Handle, HOTKEY_ID_SCREENSHOT,
                    NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    (uint)System.Windows.Forms.Keys.OemPeriod); // '.' key
            }
            
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
            if (_currentMoveX != 0 || _currentMoveY != 0)
            {
                MoveWindow(_currentMoveX, _currentMoveY);
            }
        }

        private void ResizeTimer_Tick(object? sender, EventArgs e)
        {
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
            
            // Start the startup label animation (content panels will appear after label hides)
            StartStartupAnimation();
            
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
                        Dispatcher.BeginInvoke(new Action(() => ScrollToBottom()), System.Windows.Threading.DispatcherPriority.Loaded);
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
                        var border = this.FindName("MainBorder") as System.Windows.FrameworkElement;
                        if (border != null)
                        {
                            StartContinuousBorderAnimation(border);
                        }

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
                            System.Windows.Media.Color.FromArgb(128, 0x3F, 0x3F, 0x46), 0.3)); // Fade in
                        gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(180, 0x3F, 0x3F, 0x46), 0.5)); // Most visible in middle
                        gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                            System.Windows.Media.Color.FromArgb(128, 0x3F, 0x3F, 0x46), 0.7)); // Fade out
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

                // Create a simple rotating gradient brush with a bright light section
                // The border itself stays dark, only the light rotates
                var gradientBrush = new System.Windows.Media.LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1),
                    MappingMode = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox
                };

                // Create gradient with two short bright light sections - ONLY the lights have color
                // Everything else is fully transparent so the dark border shows through
                // Each light covers ~10% of the gradient for shorter light paths
                
                // Gradient stops in order from 0.0 to 1.0
                // First light section (0.1 - 0.2), second light (0.5 - 0.6)
                
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.0)); // Transparent (wraps from end)
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.04)); // Transparent
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(180, 78, 201, 176), 0.08)); // First light: fade to bright
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.12)); // First light: brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.16)); // First light: brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(180, 78, 201, 176), 0.2)); // First light: fade out
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.24)); // Transparent
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.28)); // Transparent
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(180, 78, 201, 176), 0.32)); // Second light: fade to bright
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.36)); // Second light: brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(255, 120, 220, 200), 0.4)); // Second light: brightest
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(180, 78, 201, 176), 0.44)); // Second light: fade out
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 0.48)); // Transparent
                gradientBrush.GradientStops.Add(new System.Windows.Media.GradientStop(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 1.0)); // Transparent (wraps to 0.0)

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

        private void StartStartupAnimation()
        {
            try
            {
                if (StartupLabelContainer == null) return;

                // Animate only the BorderPath rectangle from smallest to correct size
                var borderPath = this.FindName("BorderPath") as System.Windows.Shapes.Rectangle;
                if (borderPath != null)
                {
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

                    borderPathTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnimation);
                    borderPathTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnimation);
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
                    StartupLabelContainer.RenderTransform = transform;
                }

                // Start fade in and slide up
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
                    
                    // Also inject CSS to make body background transparent for web pages
                    StealthBrowser.CoreWebView2.DOMContentLoaded += (s, args) =>
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
                        StealthBrowser.CoreWebView2.ExecuteScriptAsync(script);
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
                        Dispatcher.BeginInvoke(new Action(() => SendLastCaptionToWebView()));
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
                    // Get the current URL from the TextBox directly (not from binding to avoid timing issues)
                    string url = AddressBar?.Text?.Trim() ?? viewModel.BrowserUrl?.Trim() ?? string.Empty;
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
        }

        private void ResizeWindow(double deltaWidth, double deltaHeight)
        {
            this.Width = Math.Max(300, this.Width + deltaWidth);
            this.Height = Math.Max(200, this.Height + deltaHeight);
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
                CaptionScrollViewer.ScrollToEnd();
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

        protected override void OnClosed(EventArgs e)
        {
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

            // Remove hook
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
            }

            base.OnClosed(e);
        }
    }
}
