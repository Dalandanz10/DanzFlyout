using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DanzFlyout
{
    public partial class LockKeysFlyout : Window
    {
        private DispatcherTimer? _hideTimer;
        private bool _isCurrentlyOn = false;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMNCRP_DISABLED = 1;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public LockKeysFlyout()
        {
            InitializeComponent();

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                AnimateOut();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            // Kill rectangular DWM box shadow
            int renderingPolicy = DWMNCRP_DISABLED;
            DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref renderingPolicy, sizeof(int));

            ApplyPlacementSettings();
        }

        public void ApplyPlacementSettings()
        {
            try
            {
                var screens = Screen.AllScreens;
                if (screens.Length == 0) return;

                int targetIndex = Math.Clamp(SettingsConfig.Current.LockKeysMonitorIndex, 0, screens.Length - 1);
                var targetScreen = screens[targetIndex];

                var source = PresentationSource.FromVisual(this);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                double workLeft = targetScreen.WorkingArea.Left / dpiX;
                double workTop = targetScreen.WorkingArea.Top / dpiY;
                double workWidth = targetScreen.WorkingArea.Width / dpiX;
                double workHeight = targetScreen.WorkingArea.Height / dpiY;

                double workRight = workLeft + workWidth;
                double workBottom = workTop + workHeight;

                double w = (ActualWidth > 0) ? ActualWidth : Width;
                double h = (ActualHeight > 0) ? ActualHeight : Height;
                if (double.IsNaN(w) || w <= 0) w = 324;
                if (double.IsNaN(h) || h <= 0) h = 118;

                // Window has a 28px transparent margin for the shadow; subtract it so the card sits 24px from the edge
                const double margin = 24.0 - 28.0;

                switch (SettingsConfig.Current.LockKeysPlacement)
                {
                    case 0: // Bottom-Right
                        Left = workRight - w - margin;
                        Top = workBottom - h - margin;
                        break;
                    case 1: // Bottom-Left
                        Left = workLeft + margin;
                        Top = workBottom - h - margin;
                        break;
                    case 2: // Top-Right
                        Left = workRight - w - margin;
                        Top = workTop + margin;
                        break;
                    case 3: // Top-Left
                        Left = workLeft + margin;
                        Top = workTop + margin;
                        break;
                    case 5: // Bottom-Center
                        Left = workLeft + (workWidth - w) / 2.0;
                        Top = workBottom - h - margin;
                        break;
                    case 6: // Center-Left
                        Left = workLeft + margin;
                        Top = workTop + (workHeight - h) / 2.0;
                        break;
                    case 7: // Center-Right
                        Left = workRight - w - margin;
                        Top = workTop + (workHeight - h) / 2.0;
                        break;
                    default: // 4: Top-Center
                        Left = workLeft + (workWidth - w) / 2.0;
                        Top = workTop + margin;
                        break;
                }
            }
            catch
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Left + (workArea.Width - (ActualWidth > 0 ? ActualWidth : 218)) / 2.0;
                Top = workArea.Top + 24;
            }
        }

        public void ShowStatus(string keyName, bool isOn, bool force = false)
        {
            if (!force && !SettingsConfig.Current.EnableLockKeysFlyout) return;

            Dispatcher.Invoke(() =>
            {
                ApplyPlacementSettings();

                var text = FindName("LockText") as TextBlock;
                if (text != null)
                    text.Text = keyName;

                var state = FindName("LockState") as TextBlock;
                if (state != null)
                    state.Text = isOn ? "On" : "Off";

                AnimateToggle(isOn);
                _isCurrentlyOn = isOn;

                _hideTimer?.Stop();
                AnimateIn();
                _hideTimer?.Start();
            });
        }

        public void PreviewFlyout(bool force = false)
        {
            ShowStatus("Caps Lock", true, force);
        }

        private void AnimateToggle(bool isOn)
        {
            var track = FindName("ToggleTrack") as Border;
            var thumb = FindName("ToggleThumb") as Border;
            var thumbTranslate = FindName("ThumbTranslate") as TranslateTransform;

            if (track == null || thumb == null || thumbTranslate == null) return;

            // Travel distance inside the 52px track
            double targetX = isOn ? 24.0 : 0.0;

            var slideAnim = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            thumbTranslate.BeginAnimation(TranslateTransform.XProperty, slideAnim);

            Color trackTargetColor = isOn ? Color.FromArgb(120, 30, 160, 220) : Color.FromArgb(35, 255, 255, 255);
            Color trackBorderColor = isOn ? Color.FromArgb(190, 96, 205, 255) : Color.FromArgb(50, 255, 255, 255);

            Color thumbTargetColor = isOn ? Color.FromArgb(245, 255, 255, 255) : Color.FromArgb(110, 255, 255, 255);
            Color thumbBorderColor = isOn ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255);

            var trackAnim = new ColorAnimation(trackTargetColor, TimeSpan.FromMilliseconds(200));
            var trackBorderAnim = new ColorAnimation(trackBorderColor, TimeSpan.FromMilliseconds(200));
            var thumbAnim = new ColorAnimation(thumbTargetColor, TimeSpan.FromMilliseconds(200));
            var thumbBorderAnim = new ColorAnimation(thumbBorderColor, TimeSpan.FromMilliseconds(200));

            track.Background = new SolidColorBrush(isOn ? Color.FromArgb(35, 255, 255, 255) : Color.FromArgb(120, 30, 160, 220));
            track.Background.BeginAnimation(SolidColorBrush.ColorProperty, trackAnim);

            track.BorderBrush = new SolidColorBrush(isOn ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(190, 96, 205, 255));
            track.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, trackBorderAnim);

            thumb.Background = new SolidColorBrush(isOn ? Color.FromArgb(110, 255, 255, 255) : Color.FromArgb(245, 255, 255, 255));
            thumb.Background.BeginAnimation(SolidColorBrush.ColorProperty, thumbAnim);

            thumb.BorderBrush = new SolidColorBrush(isOn ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(255, 255, 255, 255));
            thumb.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, thumbBorderAnim);
        }

        private void AnimateIn()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch { }

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var slideDown = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };

            var translate = FindName("PillTranslate") as TranslateTransform;
            BeginAnimation(OpacityProperty, fadeIn);
            translate?.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }

        private void AnimateOut()
        {
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var slideUp = new DoubleAnimation(-8.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var translate = FindName("PillTranslate") as TranslateTransform;
            BeginAnimation(OpacityProperty, fadeOut);
            translate?.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }
    }
}
