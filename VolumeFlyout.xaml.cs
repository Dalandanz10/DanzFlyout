using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DanzFlyout
{
    public partial class VolumeFlyout : Window, IMMNotificationClient
    {
        private MMDeviceEnumerator? _deviceEnumerator;
        private MMDevice? _defaultPlaybackDevice;
        private DispatcherTimer? _hideTimer;
        private bool _isLoaded = false;
        private float _lastKnownVolume = -1f;
        private bool _lastKnownMute = false;

        private readonly object _endpointLock = new();
        private int _refreshQueued = 0;
        private bool _callbackRegistered = false;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public VolumeFlyout()
        {
            InitializeComponent();
            Loaded += VolumeFlyout_Loaded;
            Closed += VolumeFlyout_Closed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        private void VolumeFlyout_Loaded(object sender, RoutedEventArgs e)
        {
            PositionFlyout();
            QueueEndpointRefresh(0);

            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.0)
            };
            _hideTimer.Tick += (s, args) => HideFlyout();

            _isLoaded = true;
        }

        public void PositionFlyout()
        {
            try
            {
                var screens = Screen.AllScreens;
                int monitorIdx = Math.Clamp(SettingsConfig.Current?.VolumeFlyoutMonitorIndex ?? 0, 0, screens.Length - 1);
                var screen = screens[monitorIdx];

                var source = PresentationSource.FromVisual(this);
                double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                double screenLeft = screen.WorkingArea.Left / dpiX;
                double screenTop = screen.WorkingArea.Top / dpiY;
                double screenWidth = screen.WorkingArea.Width / dpiX;
                double screenHeight = screen.WorkingArea.Height / dpiY;
                double screenBottom = screen.WorkingArea.Bottom / dpiY;

                double flyoutWidth = ActualWidth > 0 ? ActualWidth : Width;
                double flyoutHeight = ActualHeight > 0 ? ActualHeight : Height;

                // The window has a 28px transparent margin (room for the soft shadow),
                // so subtract it to keep the visible card 24px from the screen edge.
                const double ShadowPad = 28.0;
                const double Edge = 24.0;
                double o = Edge - ShadowPad;

                int placement = SettingsConfig.Current?.VolumeFlyoutPlacement ?? 1;

                // 0: TopLeft, 1: TopCenter, 2: TopRight, 3: CenterLeft,
                // 4: CenterRight, 5: BottomLeft, 6: BottomCenter, 7: BottomRight
                switch (placement)
                {
                    case 0:
                        Left = screenLeft + o;
                        Top = screenTop + o;
                        break;
                    case 1:
                        Left = screenLeft + (screenWidth - flyoutWidth) / 2.0;
                        Top = screenTop + o;
                        break;
                    case 2:
                        Left = screenLeft + screenWidth - flyoutWidth - o;
                        Top = screenTop + o;
                        break;
                    case 3:
                        Left = screenLeft + o;
                        Top = screenTop + (screenHeight - flyoutHeight) / 2.0;
                        break;
                    case 4:
                        Left = screenLeft + screenWidth - flyoutWidth - o;
                        Top = screenTop + (screenHeight - flyoutHeight) / 2.0;
                        break;
                    case 5:
                        Left = screenLeft + o;
                        Top = screenBottom - flyoutHeight - o;
                        break;
                    case 7:
                        Left = screenLeft + screenWidth - flyoutWidth - o;
                        Top = screenBottom - flyoutHeight - o;
                        break;
                    case 6:
                    default:
                        Left = screenLeft + (screenWidth - flyoutWidth) / 2.0;
                        Top = screenBottom - flyoutHeight - o;
                        break;
                }
            }
            catch { }
        }

        public void PreviewFlyout(bool force = false)
{
    if (FullscreenHelper.IsGameOrFullscreenActive())
    {
        return; // Suppress volume popup while playing in League or fullscreen
    }

    Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_lastKnownVolume >= 0f)
            UpdateUI((int)Math.Round(_lastKnownVolume * 100.0), _lastKnownMute);

        ShowFlyout(force);
    }));
}

        // All audio COM work runs on a pool thread. Doing it on the UI thread while Windows was delivering
        // a notification (which itself waited for the UI thread) deadlocked the whole app on device switches.
        private void QueueEndpointRefresh(int delayMs)
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;

            Task.Run(async () =>
            {
                try
                {
                    if (delayMs > 0) await Task.Delay(delayMs);
                    Interlocked.Exchange(ref _refreshQueued, 0);
                    InitVolumeEndpoint();
                }
                catch
                {
                    Interlocked.Exchange(ref _refreshQueued, 0);
                }
            });
        }

        private void InitVolumeEndpoint()
        {
            try
            {
                lock (_endpointLock)
                {
                    _deviceEnumerator ??= new MMDeviceEnumerator();

                    // Register the callback exactly once. Re-registering on every device change
                    // (and from inside a notification) is what used to hang the app.
                    if (!_callbackRegistered)
                    {
                        _deviceEnumerator.RegisterEndpointNotificationCallback(this);
                        _callbackRegistered = true;
                    }

                    var oldDevice = _defaultPlaybackDevice;
                    _defaultPlaybackDevice = null;
                    if (oldDevice != null)
                        DisposeDeviceInBackground(oldDevice);

                    var device = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (device == null) return;

                    device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                    float volume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                    bool muted = device.AudioEndpointVolume.Mute;

                    _defaultPlaybackDevice = device;
                    _lastKnownVolume = volume;
                    _lastKnownMute = muted;

                    int percent = (int)Math.Round(volume * 100.0);
                    Dispatcher.BeginInvoke(new Action(() => UpdateUI(percent, muted)));
                }
            }
            catch { }
        }

        private void DisposeDeviceInBackground(MMDevice device)
        {
            var thread = new Thread(() =>
            {
                try { device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification; } catch { }
                try { device.Dispose(); } catch { }
            })
            { IsBackground = true, Name = "DanzFlyout.VolumeDeviceDispose" };
            thread.Start();
        }

        private void DetachVolumeListener()
        {
            MMDevice? old;
            lock (_endpointLock)
            {
                old = _defaultPlaybackDevice;
                _defaultPlaybackDevice = null;
            }

            if (old != null)
                DisposeDeviceInBackground(old);
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isLoaded) return;
                if (!SettingsConfig.Current.EnableVolumeFlyout) return;

                int percent = (int)Math.Round(data.MasterVolume * 100.0);
                bool isMuted = data.Muted;

                if (Math.Abs(data.MasterVolume - _lastKnownVolume) > 0.002f || isMuted != _lastKnownMute)
                {
                    _lastKnownVolume = data.MasterVolume;
                    _lastKnownMute = isMuted;

                    UpdateUI(percent, isMuted);
                    ShowFlyout();
                }
            }));
        }

        private void UpdateUI(int percent, bool isMuted)
        {
            if (TxtVolume != null)
                TxtVolume.Text = isMuted ? "Mute" : $"{percent}%";

            if (TxtVolIcon != null)
            {
                // Segoe MDL2 / Fluent glyphs: mute, volume 0..3
                if (isMuted)
                    TxtVolIcon.Text = "\uE74F";
                else if (percent == 0)
                    TxtVolIcon.Text = "\uE992";
                else if (percent < 33)
                    TxtVolIcon.Text = "\uE993";
                else if (percent < 66)
                    TxtVolIcon.Text = "\uE994";
                else
                    TxtVolIcon.Text = "\uE995";
            }

            if (VolKnob != null)
                VolKnob.Opacity = (isMuted || percent == 0) ? 0.0 : 1.0;

            if (VolFill != null)
            {
                double trackWidth = (VolFill.Parent is FrameworkElement parent && parent.ActualWidth > 0)
                    ? parent.ActualWidth
                    : 190.0;

                double targetWidth = isMuted ? 0 : (percent / 100.0) * trackWidth;

                var fillAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(100))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                VolFill.BeginAnimation(WidthProperty, fillAnim);
            }
        }

        public void ShowFlyout(bool force = false)
{
    // Block the flyout if a game/fullscreen app is active OR if disabled in settings
    if (FullscreenHelper.IsGameOrFullscreenActive() || (!force && !SettingsConfig.Current.EnableVolumeFlyout))
    {
        _hideTimer?.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        return;
    }

    _hideTimer?.Stop();
    PositionFlyout();
    // ... rest of the method stays the same

            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fadeIn);

            _hideTimer?.Start();
        }

        public void HideFlyout()
        {
            _hideTimer?.Stop();

            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                QueueEndpointRefresh(300);
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void VolumeFlyout_Closed(object? sender, EventArgs e)
        {
            DetachVolumeListener();

            MMDeviceEnumerator? enumerator;
            lock (_endpointLock)
            {
                enumerator = _deviceEnumerator;
                _deviceEnumerator = null;
                _callbackRegistered = false;
            }

            if (enumerator != null)
            {
                var thread = new Thread(() =>
                {
                    try { enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
                    try { enumerator.Dispose(); } catch { }
                })
                { IsBackground = true, Name = "DanzFlyout.VolumeEnumDispose" };
                thread.Start();
            }
        }
    }
}