using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using StealthInterviewAssistant.Interop;

namespace StealthInterviewAssistant.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
            this.Opacity = 0; // Start invisible for fade-in
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Hide from taskbar and Alt+Tab
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                int exStyle = NativeMethods.GetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
                exStyle &= ~NativeMethods.WS_EX_APPWINDOW;
                NativeMethods.SetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE, exStyle);
            }
        }

        public void StartFadeIn()
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.3)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            this.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        public void StartFadeOut(Action? onCompleted = null)
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.3)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            
            if (onCompleted != null)
            {
                fadeOut.Completed += (s, e) => onCompleted();
            }
            
            this.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }
}

