using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Dsp;
using NAudio.Wave;

namespace DanzFlyout
{
    public partial class TaskbarVisualizer : Window, IMMNotificationClient
    {
        private MMDeviceEnumerator? _deviceEnumerator;
        private WasapiLoopbackCapture? _capture;
        private DispatcherTimer? _renderTimer;
        private DispatcherTimer? _fullscreenCheckTimer;
        private DispatcherTimer? _nativeFlyoutTimer;

        private readonly SemaphoreSlim _audioInitLock = new(1, 1);
        private int _isReconnecting = 0;
        private int _reconnectRequested = 0;
        private int _forceReconnect = 0;
        private int _audioGeneration = 0;
        private long _lastAudioDataTicks = 0;
        private long _lastWatchdogKick = 0;
        private string? _captureDeviceId;
        private readonly object _captureSwapLock = new();
        private readonly object _enumLock = new();
        private volatile bool _stopping = false;
        private DispatcherTimer? _audioWatchdogTimer;
        private Thread? _flyoutSuppressThread;
        private Thread? _audioWatchdogThread;
        private long _emptyBufferCount = 0;
        private long _uiHeartbeatTicks = 0;
        private int _uiPingPending = 0;
        private long _lastUiWarn = 0;
        private long _lastZLog = 0;
        private const int ConnectTimeoutMs = 4000;
        private long _audioDebugDataCount = 0;

        private const double TotalVisualizerWidth = 94.0;

        // 1024-point FFT with 512 sliding step
        private const int FftLength = 1024;
        private const int FftM = 10;
        private readonly Complex[] _fftBufferL = new Complex[FftLength];
        private readonly Complex[] _fftBufferR = new Complex[FftLength];
        private readonly float[] _fftMagnitudes = new float[FftLength / 2];
        private readonly float[] _fftPublishedSnapshot = new float[FftLength / 2];
        private readonly float[] _fftRenderBuffer = new float[FftLength / 2];
        private int _fftPos = 0;
        private readonly object _fftLock = new();
        private readonly object _fftSnapshotLock = new();

        // EDM Ballistics & Transient Physics State
        private float[] _barHeight = new float[64];
        private float[] _barVelocity = new float[64];
        private double[] _barOpacities = new double[64];

        private float _prevSub;
        private float _prevHigh;
        private float _subRef = 1e-5f;
        private float _highRef = 1e-5f;

        private float _kickEnvelope = 0f;
        private float _snareEnvelope = 0f;
        private float _visualPeakRef = 0.05f;
        private float[] _barReference = new float[64];

        // Visual Effects State
        private double _bassPumpScale = 1.0;
        private double _currentGlowBlur = 5.0;
        private DateTime _lastFrameTime = DateTime.UtcNow;

        // Peak Cap Physics State
        private double[] _peakCapHeights = new double[64];
        private double[] _peakCapVelocities = new double[64];
        private int[] _peakCapHold = new int[64];

        // Dynamic Color Shift State
        private double _colorPhase = 0.0;
        private Color _colorA = Color.FromRgb(59, 130, 246);
        private Color _colorB = Color.FromRgb(239, 68, 68);
        private LinearGradientBrush? _titleGradientBrush;
        private Storyboard? _titleColorWaveStoryboard;
        // Fluid Wave renderer state. The existing bar renderer remains untouched.
        private readonly List<System.Windows.Shapes.Path> _fluidWavePaths = new();
        private double _fluidWavePhase = 0.0;

        // 2-Color Adaptive Palette
        private AccentExtractor.Palette _adaptivePalette = new()
        {
            Primary = Color.FromRgb(56, 189, 248),
            Secondary = Color.FromRgb(94, 234, 212)
        };

        private string _lastTitle = string.Empty;
        private bool _isPlaying = false;
        private bool _isVisible = false;
        private bool _isFullscreenBlocked = false;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // Windows 10 native SMTC / Volume flyout APIs.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(
            IntPtr hwndParent,
            IntPtr hwndChildAfter,
            string lpszClass,
            string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(
            IntPtr windowHandle,
            int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out int processId);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(
            IntPtr hWnd,
            int nIndex,
            int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(
            IntPtr hWnd,
            int nIndex,
            int dwNewLong);


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RealGetWindowClass(IntPtr hwnd, System.Text.StringBuilder pszType, uint cchType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_HWNDPREV = 3;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public TaskbarVisualizer()
        {
            InitializeComponent();
            Loaded += TaskbarVisualizer_Loaded;
            Closed += (s, e) => StopAudio();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            RebuildBars();
            ApplySettingsLayout();
        }

        public void RebuildBars()
        {
            var panel = FindName("BarsPanel") as StackPanel;
            var capsCanvas = FindName("PeakCapsCanvas") as Canvas;
            var fluidCanvas = FindName("FluidWaveCanvas") as Canvas;
            if (panel == null || capsCanvas == null || fluidCanvas == null) return;

            panel.Children.Clear();
            capsCanvas.Children.Clear();
            fluidCanvas.Children.Clear();
            _fluidWavePaths.Clear();

            int requestedCount = Math.Clamp(SettingsConfig.Current.VisualizerBarCount, 4, 48);
            bool isBottom = SettingsConfig.Current.VisualizerStyle == "BottomBars";
            bool isFluidWave = SettingsConfig.Current.VisualizerStyle == "FluidWave";
            int count = isFluidWave ? Math.Clamp(requestedCount, 4, 24) : requestedCount;

            panel.Width = TotalVisualizerWidth;
            panel.VerticalAlignment = isBottom ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            panel.Margin = isBottom ? new Thickness(0, 0, 0, 1) : new Thickness(0);
            panel.Opacity = isFluidWave ? 0.0 : 1.0;
            capsCanvas.Opacity = isFluidWave ? 0.0 : 1.0;
            fluidCanvas.Opacity = isFluidWave ? 1.0 : 0.0;

            _barHeight = new float[count];
            _barVelocity = new float[count];
            _barOpacities = new double[count];
            _peakCapHeights = new double[count];
            _peakCapVelocities = new double[count];
            _peakCapHold = new int[count];

            if (isFluidWave)
            {
                for (int i = 0; i < count; i++)
                {
                    double layer = count <= 1 ? 0.0 : (double)i / (count - 1);
                    var path = new System.Windows.Shapes.Path
                    {
                        Fill = null,
                        StrokeThickness = 0.65 + ((1.0 - layer) * 0.45),
                        Opacity = 0.12 + ((1.0 - layer) * 0.88),
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Effect = new DropShadowEffect
                        {
                            BlurRadius = 4.5,
                            ShadowDepth = 0,
                            Opacity = 0.35,
                            Color = Color.FromRgb(124, 140, 255)
                        }
                    };

                    fluidCanvas.Children.Add(path);
                    _fluidWavePaths.Add(path);
                }

                ApplyCurrentTheme();
                return;
            }

            double slotWidth = TotalVisualizerWidth / count;
            double barWidth = Math.Clamp(slotWidth * 0.62, 1.2, 3.5);
            double horizontalMargin = Math.Max(0.4, (slotWidth - barWidth) / 2.0);

            for (int i = 0; i < count; i++)
            {
                CornerRadius barRadius;
                if (i == 0)
                    barRadius = new CornerRadius(99, 0.8, 0.8, 99);
                else if (i == count - 1)
                    barRadius = new CornerRadius(0.8, 99, 99, 0.8);
                else
                    barRadius = new CornerRadius(0.8);

                var bar = new Border
                {
                    Width = barWidth,
                    Height = 3.0,
                    CornerRadius = barRadius,
                    Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0),
                    Opacity = 0.0,
                    VerticalAlignment = isBottom ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 5,
                        ShadowDepth = 0,
                        Opacity = 0.35,
                        Color = Color.FromRgb(94, 234, 212)
                    }
                };
                panel.Children.Add(bar);

                var cap = new Border
                {
                    Width = barWidth,
                    Height = 1.2,
                    CornerRadius = new CornerRadius(0.6),
                    Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                    Opacity = 0.0,
                    IsHitTestVisible = false
                };
                capsCanvas.Children.Add(cap);
                Canvas.SetLeft(cap, (i * slotWidth) + horizontalMargin);
            }

            ApplyCurrentTheme();
        }

        public void ApplySettingsLayout()
{
    try
    {
        var screens = Screen.AllScreens;
        int targetIndex = Math.Clamp(SettingsConfig.Current.VisualizerMonitorIndex, 0, screens.Length - 1);
        var targetScreen = screens[targetIndex];

        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        double screenLeft = targetScreen.Bounds.Left / dpiX;
        double screenWidth = targetScreen.Bounds.Width / dpiX;
        double screenBottom = targetScreen.Bounds.Bottom / dpiY;
        double taskbarHeight = (targetScreen.Bounds.Bottom - targetScreen.WorkingArea.Bottom) / dpiY;
        if (taskbarHeight <= 0) taskbarHeight = 40;

        bool isFluidWave = SettingsConfig.Current.VisualizerStyle == "FluidWave";

        // Keep standard height identical across modes to prevent bottom clipping
        double visualizerHeight = 40.0;
        Height = visualizerHeight;

        var visualizerContainer = FindName("VisualizerContainer") as Grid;
        var fluidCanvas = FindName("FluidWaveCanvas") as Canvas;

        if (visualizerContainer != null) visualizerContainer.Height = 26.0;
        if (fluidCanvas != null) fluidCanvas.Height = 26.0;

        double totalWidth = ActualWidth > 0 ? ActualWidth : 300;
        double baseCenterLeft = screenLeft + (screenWidth - totalWidth) / 2.0;

        // X Axis: Clean centering + user offset
        Left = baseCenterLeft + SettingsConfig.Current.VisualizerOffset;

        // Y Axis: Perfectly center the 40px window inside the taskbar height
        Top = (screenBottom - taskbarHeight) + (taskbarHeight - visualizerHeight) / 2.0;

        var titleCanvas = FindName("TitleCanvas") as Canvas;
        if (titleCanvas != null)
        {
            titleCanvas.Margin = new Thickness(0, 0, Math.Max(8, SettingsConfig.Current.VisualizerTitleSpacing), 0);
        }

        ApplyCurrentTheme();
    }
    catch { }
}

        public void ApplyCurrentTheme()
        {
            try
            {
                _colorA = (Color)ColorConverter.ConvertFromString(SettingsConfig.Current.DynamicColorA);
                _colorB = (Color)ColorConverter.ConvertFromString(SettingsConfig.Current.DynamicColorB);
            }
            catch
            {
                _colorA = Color.FromRgb(59, 130, 246);
                _colorB = Color.FromRgb(239, 68, 68);
            }

            var panel = FindName("BarsPanel") as StackPanel;
            var fluidCanvas = FindName("FluidWaveCanvas") as Canvas;
            bool isFluidWave = SettingsConfig.Current.VisualizerStyle == "FluidWave";

            if (isFluidWave)
            {
                ApplyFluidWaveTheme();
                return;
            }

            if (panel == null) return;
            int count = panel.Children.Count;
            if (count == 0) return;

            string theme = SettingsConfig.Current.VisualizerTheme;

            if (theme == "Adaptive")
            {
                // Lift brightness so darker album covers don't disappear into the taskbar
                Color cWing = EnsureTaskbarVibrancy(_adaptivePalette.Primary);
                Color cCenter = EnsureTaskbarVibrancy(_adaptivePalette.Secondary);

                // Keep the extracted palette roles intact.
                // Do not swap colors based on brightness: the album's primary
                // color should remain in the center and the secondary color
                // should remain toward the sides.
                // EnsureTaskbarVibrancy already lifts dark colors enough to
                // remain visible against the dark taskbar.

                int center = count / 2;
                for (int i = 0; i < count; i++)
                {
                    if (panel.Children[i] is Border bar)
                    {
                        double dist = (double)Math.Abs(i - center) / Math.Max(1, center);

                        // Smooth cosine blend across the span
                        double blend = (1.0 - Math.Cos(dist * Math.PI)) / 2.0;

                        byte r = (byte)(cCenter.R + (cWing.R - cCenter.R) * blend);
                        byte g = (byte)(cCenter.G + (cWing.G - cCenter.G) * blend);
                        byte b = (byte)(cCenter.B + (cWing.B - cCenter.B) * blend);
                        var barColor = Color.FromRgb(r, g, b);

                        bar.Background = new SolidColorBrush(barColor);
                        if (bar.Effect is DropShadowEffect glow)
                        {
                            glow.Color = barColor;
                            glow.Opacity = 0.55;
                        }
                    }
                }
            }
            else if (theme != "Dynamic")
            {
                Color chosenColor = theme switch
                {
                    "Magenta" => Color.FromRgb(255, 75, 110),
                    "Neon Blue" => Color.FromRgb(96, 165, 250),
                    "Amber" => Color.FromRgb(251, 191, 36),
                    "Custom" => (Color)ColorConverter.ConvertFromString(SettingsConfig.Current.CustomHexColor),
                    _ => Color.FromRgb(94, 234, 212)
                };
                SetVisualizerColor(chosenColor);
            }
        }

        private void ApplyFluidWaveTheme()
        {
            Color primary;
            Color secondary;

            if (SettingsConfig.Current.VisualizerTheme == "Adaptive")
            {
                primary = EnsureTaskbarVibrancy(_adaptivePalette.Primary);
                secondary = EnsureTaskbarVibrancy(_adaptivePalette.Secondary);
            }
            else if (SettingsConfig.Current.VisualizerTheme == "Dynamic")
            {
                primary = EnsureTaskbarVibrancy(_colorA);
                secondary = EnsureTaskbarVibrancy(_colorB);
            }
            else
            {
                primary = SettingsConfig.Current.VisualizerTheme switch
                {
                    "Magenta" => Color.FromRgb(255, 75, 110),
                    "Neon Blue" => Color.FromRgb(96, 165, 250),
                    "Amber" => Color.FromRgb(251, 191, 36),
                    "Custom" => (Color)ColorConverter.ConvertFromString(SettingsConfig.Current.CustomHexColor),
                    _ => Color.FromRgb(94, 234, 212)
                };
                secondary = primary;
            }

            int count = _fluidWavePaths.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                double t = count <= 1 ? 0.0 : (double)i / (count - 1);
                // Center layers are brighter; outer layers stay softer.
                double centerWeight = 1.0 - Math.Abs((t * 2.0) - 1.0);
                byte r = (byte)(secondary.R + (primary.R - secondary.R) * centerWeight);
                byte g = (byte)(secondary.G + (primary.G - secondary.G) * centerWeight);
                byte b = (byte)(secondary.B + (primary.B - secondary.B) * centerWeight);
                var c = Color.FromRgb(r, g, b);

                var path = _fluidWavePaths[i];
                path.Stroke = new SolidColorBrush(c);
                if (path.Effect is DropShadowEffect glow) glow.Color = c;
            }
        }

        private static Color EnsureTaskbarVibrancy(Color c)
        {
            AccentExtractor.RgbToHsv(c.R, c.G, c.B, out float h, out float s, out float v);

            // Preserve the album's real hue AND saturation.
            // Never force low-saturation colors to become colorful:
            // white/gray/black artwork must stay neutral instead of
            // turning into artificial reds/oranges.
            //
            // Only lift brightness when the color is too dark to be
            // visible against the taskbar.
            if (v < 0.82f)
                v = 0.85f;

            return AccentExtractor.HsvToRgb(h, s, v);
        }

        private void TaskbarVisualizer_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySettingsLayout();
            StartAudio();

            _lastFrameTime = DateTime.UtcNow;
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _renderTimer.Tick += RenderFrame;
            _renderTimer.Start();

            _fullscreenCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _fullscreenCheckTimer.Tick += CheckFullscreenState;
            _fullscreenCheckTimer.Start();

            // Keep Spotify/media integration enabled while suppressing only
            // the Windows 10 native black media/volume flyout.
            // IMPORTANT: this runs on its own background thread. It sends window messages to
            // explorer.exe, and if explorer is busy switching audio devices, doing that on the UI
            // thread froze the whole visualizer.
           _flyoutSuppressThread = new Thread(() =>
{
    bool lastAppliedState = false;

    while (!_stopping)
    {
        try
        {
            bool shouldHide = SettingsConfig.Current.HideWindowsMediaFlyout;

            if (shouldHide)
            {
                HideWindowsNativeMediaFlyout();
                lastAppliedState = true;
            }
            else if (lastAppliedState)
            {
                RestoreWindowsNativeMediaFlyout();
                lastAppliedState = false;
            }
        }
        catch { }

        Thread.Sleep(250);
    }

    if (lastAppliedState)
    {
        RestoreWindowsNativeMediaFlyout();
    }
})
{
    IsBackground = true,
    Name = "DanzFlyout.FlyoutSuppress"
};
_flyoutSuppressThread.Start();

            // Audio watchdog (background thread, never touches the UI thread).
            _lastAudioDataTicks = Environment.TickCount64;
            _uiHeartbeatTicks = Environment.TickCount64;
            _audioWatchdogThread = new Thread(AudioWatchdogLoop)
            {
                IsBackground = true,
                Name = "DanzFlyout.AudioWatchdog"
            };
            _audioWatchdogThread.Start();
        }

        // Clicking the taskbar (e.g. the volume icon to change output device) raises the taskbar above
        // our topmost window, hiding the visualizer behind it. Detect that and put it back on top.
        private bool IsTaskbarAboveUs(IntPtr hwnd)
        {
            IntPtr w = GetWindow(hwnd, GW_HWNDPREV);

            for (int i = 0; i < 400 && w != IntPtr.Zero; i++)
            {
                string cls = GetWindowClassName(w);
                if (cls.StartsWith("Shell_TrayWnd", StringComparison.Ordinal) ||
                    cls.StartsWith("Shell_SecondaryTrayWnd", StringComparison.Ordinal))
                    return true;

                w = GetWindow(w, GW_HWNDPREV);
            }

            return false;
        }

        private void KeepAboveTaskbar()
        {
            try
            {
                if (!_isVisible || _isFullscreenBlocked) return;

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                if (!IsTaskbarAboveUs(hwnd)) return;

                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);

                long now = Environment.TickCount64;
                if (now - _lastZLog > 5000)
                {
                    _lastZLog = now;
                    AudioDebug("Z-order: the taskbar was above the visualizer, put it back on top.");
                }
            }
            catch { }
        }

        private void CheckFullscreenState(object? sender, EventArgs e)
        {
            KeepAboveTaskbar();

            bool isGameCovering = IsGameCoveringVisualizerMonitor();

            if (isGameCovering && !_isFullscreenBlocked)
            {
                _isFullscreenBlocked = true;
                if (_isVisible) FadeOut();
            }
            else if (!isGameCovering && _isFullscreenBlocked)
            {
                _isFullscreenBlocked = false;
                if (_isPlaying && !_isVisible) FadeIn();
            }
        }

        private bool IsGameCoveringVisualizerMonitor()
        {
            try
            {
                var screens = Screen.AllScreens;
                int targetIndex = Math.Clamp(SettingsConfig.Current.VisualizerMonitorIndex, 0, screens.Length - 1);
                var screen = screens[targetIndex];

                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == GetDesktopWindow() || fg == GetShellWindow())
                    return false;

                if (GetWindowRect(fg, out RECT fgRect))
                {
                    bool coversScreen = (fgRect.Left <= screen.Bounds.Left &&
                                         fgRect.Top <= screen.Bounds.Top &&
                                         fgRect.Right >= screen.Bounds.Right &&
                                         fgRect.Bottom >= screen.Bounds.Bottom);

                    if (coversScreen)
                    {
                        string className = GetWindowClassName(fg);
                        if (className.Contains("Progman") || className.Contains("WorkerW") || className.Contains("Shell_TrayWnd"))
                            return false;

                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private string GetWindowClassName(IntPtr hWnd)
        {
            try
            {
                System.Text.StringBuilder className = new System.Text.StringBuilder(256);
                RealGetWindowClass(hWnd, className, 256);
                return className.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IntPtr SetWindowLongPtr(
            IntPtr hWnd,
            int nIndex,
            int dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);

            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong));
        }

        // Windows 11 Modern Flyout class names
private static readonly string[] Win11FlyoutClasses = new[]
{
    "XamlExplorerHostIslandWindow", // Win 11 modern media pill host
};

private void HideWindowsNativeMediaFlyout()
{
    try
    {
        IntPtr hWndHost = IntPtr.Zero;
        while ((hWndHost = FindWindowEx(IntPtr.Zero, hWndHost, "NativeHWNDHost", "")) != IntPtr.Zero)
        {
            IntPtr hWndDUI = FindWindowEx(hWndHost, IntPtr.Zero, "DirectUIHWND", "");
            if (hWndDUI == IntPtr.Zero) continue;

            // Simple, safe hide without corrupting window bitmasks
            ShowWindowAsync(hWndDUI, 0);  // SW_HIDE
            ShowWindowAsync(hWndHost, 0); // SW_HIDE
            return;
        }
    }
    catch { }
}

private void RestoreWindowsNativeMediaFlyout()
{
    try
    {
        IntPtr hWndHost = IntPtr.Zero;
        while ((hWndHost = FindWindowEx(IntPtr.Zero, hWndHost, "NativeHWNDHost", "")) != IntPtr.Zero)
        {
            IntPtr hWndDUI = FindWindowEx(hWndHost, IntPtr.Zero, "DirectUIHWND", "");
            if (hWndDUI == IntPtr.Zero) continue;

            // Remove WS_DISABLED
            unchecked
            {
                int duiStyle = GetWindowLong(hWndDUI, -16);
                SetWindowLongPtr(hWndDUI, -16, duiStyle & ~unchecked((int)0x80000000));

                int hostStyle = GetWindowLong(hWndHost, -16);
                SetWindowLongPtr(hWndHost, -16, hostStyle & ~unchecked((int)0x80000000));
            }

            // Restore visibility and tell Windows to redraw without zero-sizing
            ShowWindowAsync(hWndDUI, 4); // SW_SHOWNOACTIVATE
            ShowWindowAsync(hWndHost, 4);

            SetWindowPos(hWndHost, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW
            SetWindowPos(hWndDUI, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
            return;
        }
    }
    catch { }
}
        private static Color MakeTitleColor(Color source)
        {
            AccentExtractor.RgbToHsv(source.R, source.G, source.B, out float h, out float sat, out float value);
            value = Math.Max(value, 0.94f);
            sat = Math.Min(sat, 0.88f);
            Color boosted = AccentExtractor.HsvToRgb(h, sat, value);

            double luminance = (0.2126 * boosted.R + 0.7152 * boosted.G + 0.0722 * boosted.B) / 255.0;
            if (luminance < 0.58)
            {
                double mix = Math.Clamp((0.58 - luminance) / 0.30, 0.0, 0.42);
                boosted = Color.FromRgb(
                    (byte)(boosted.R + (255 - boosted.R) * mix),
                    (byte)(boosted.G + (255 - boosted.G) * mix),
                    (byte)(boosted.B + (255 - boosted.B) * mix));
            }
            return boosted;
        }

        // Called from Settings when the "Album-colored Track Title" toggle changes
        public void RefreshTitleStyle()
        {
            Dispatcher.Invoke(ApplyAlbumTitleGradient);
        }

        private void ApplyAlbumTitleGradient()
{
    var titleText = FindName("MiniTrackTitle") as TextBlock;
    if (titleText == null) return;

    StopTitleColorWave();

    if (!SettingsConfig.Current.AdaptiveTitleColor)
    {
        ResetAlbumTitleStyle();
        return;
    }

    Color left = MakeTitleColor(_adaptivePalette.Primary);
    Color right = MakeTitleColor(_adaptivePalette.Secondary);

    // Create a smooth album-color gradient.
    var brush = new LinearGradientBrush
    {
        StartPoint = new Point(-1.0, 0.5),
        EndPoint = new Point(1.0, 0.5)
    };

    brush.GradientStops.Add(new GradientStop(left, 0.0));
    brush.GradientStops.Add(new GradientStop(left, 0.35));
    brush.GradientStops.Add(new GradientStop(right, 0.50));
    brush.GradientStops.Add(new GradientStop(left, 0.65));
    brush.GradientStops.Add(new GradientStop(right, 1.0));

    _titleGradientBrush = brush;
    titleText.Foreground = brush;

    titleText.Effect = new DropShadowEffect
    {
        Color = Colors.Black,
        BlurRadius = 2.5,
        ShadowDepth = 0,
        Opacity = 0.88
    };

    StartTitleColorWave();
}
    private void StartTitleColorWave()
{
    if (_titleGradientBrush == null)
        return;

    StopTitleColorWave();

    var startAnimation = new PointAnimation
    {
        From = new Point(-1.5, 0.5),
        To = new Point(1.5, 0.5),
        Duration = new Duration(TimeSpan.FromSeconds(5.0)),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase
        {
            EasingMode = EasingMode.EaseInOut
        }
    };

    var endAnimation = new PointAnimation
    {
        From = new Point(-0.5, 0.5),
        To = new Point(2.5, 0.5),
        Duration = new Duration(TimeSpan.FromSeconds(5.0)),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase
        {
            EasingMode = EasingMode.EaseInOut
        }
    };

    Storyboard.SetTarget(startAnimation, _titleGradientBrush);
    Storyboard.SetTargetProperty(
        startAnimation,
        new PropertyPath(LinearGradientBrush.StartPointProperty));

    Storyboard.SetTarget(endAnimation, _titleGradientBrush);
    Storyboard.SetTargetProperty(
        endAnimation,
        new PropertyPath(LinearGradientBrush.EndPointProperty));

    _titleColorWaveStoryboard = new Storyboard();

    _titleColorWaveStoryboard.Children.Add(startAnimation);
    _titleColorWaveStoryboard.Children.Add(endAnimation);

    _titleColorWaveStoryboard.Begin();
}

private void StopTitleColorWave()
{
    if (_titleColorWaveStoryboard == null)
        return;

    try
    {
        _titleColorWaveStoryboard.Stop();
    }
    catch
    {
    }

    _titleColorWaveStoryboard = null;
}
        private void ResetAlbumTitleStyle()
{
    StopTitleColorWave();

    var titleText = FindName("MiniTrackTitle") as TextBlock;
    if (titleText == null) return;

    titleText.Foreground = new SolidColorBrush(
        Color.FromArgb(238, 255, 255, 255));

    titleText.Effect = new DropShadowEffect
    {
        Color = Colors.Black,
        BlurRadius = 2.5,
        ShadowDepth = 0,
        Opacity = 0.88
    };
}

        public void UpdateMedia(string title, BitmapSource? cover, bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = isPlaying && !string.IsNullOrWhiteSpace(title) && SettingsConfig.Current.EnableTaskbarVisualizer;

                var titleText = FindName("MiniTrackTitle") as TextBlock;
                var coverImg = FindName("MiniAlbumCover") as Image;
                var titleCanvas = FindName("TitleCanvas") as Canvas;
                var coverScale = FindName("AlbumCoverScale") as ScaleTransform;
                var titleTranslate = FindName("TitleTranslate") as TranslateTransform;

                bool isNewTrack = !string.IsNullOrWhiteSpace(title) && title != _lastTitle;

                if (titleText != null && isNewTrack)
                {
                    _lastTitle = title;
                    titleText.Text = title;

                    if (titleTranslate != null)
                    {
                        var slideAnim = new DoubleAnimation(6.0, 0.0, TimeSpan.FromMilliseconds(220))
                        {
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                        };
                        titleTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
                    }

                    if (titleCanvas != null)
                    {
                        SetupMarquee(titleText, titleCanvas);
                    }
                }

                if (coverImg != null && cover != null)
                {
                    coverImg.Source = cover;

                    if (isNewTrack && coverScale != null)
                    {
                        var scaleAnim = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(240))
                        {
                            EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
                        };
                        coverScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                        coverScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                    }

                    _adaptivePalette = AccentExtractor.ExtractPalette(cover);
                    ApplyAlbumTitleGradient();
                    if (SettingsConfig.Current.VisualizerTheme == "Adaptive")
                    {
                        ApplyCurrentTheme();
                    }
                }
                else if (cover == null)
                {
                    ResetAlbumTitleStyle();
                }

                if (_isPlaying && !_isVisible && !_isFullscreenBlocked)
                {
                    FadeIn();
                }
                else if ((!_isPlaying || _isFullscreenBlocked) && _isVisible)
                {
                    FadeOut();
                }
            });
        }

        public void CheckEnabledState()
        {
            if (!SettingsConfig.Current.EnableTaskbarVisualizer && _isVisible)
            {
                FadeOut();
            }
            else if (SettingsConfig.Current.EnableTaskbarVisualizer && _isPlaying && !_isVisible && !_isFullscreenBlocked)
            {
                FadeIn();
            }
        }

        private void FadeIn()
        {
            _isVisible = true;
            ApplySettingsLayout();

            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, anim);
        }

        private void FadeOut()
        {
            _isVisible = false;
            var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            BeginAnimation(OpacityProperty, anim);
        }

        private void SetupMarquee(TextBlock textBlock, Canvas parentCanvas)
        {
            textBlock.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(textBlock, 0);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                textBlock.Measure(new Size(double.PositiveInfinity, parentCanvas.ActualHeight));
                double textWidth = textBlock.DesiredSize.Width;
                double canvasWidth = parentCanvas.Width;

                if (textWidth > canvasWidth && canvasWidth > 0)
                {
                    double diff = textWidth - canvasWidth;
                    var anim = new DoubleAnimation
                    {
                        From = 0,
                        To = -diff - 8,
                        Duration = TimeSpan.FromSeconds(Math.Max(2.8, diff / 18.0)),
                        BeginTime = TimeSpan.FromSeconds(1.5),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    textBlock.BeginAnimation(Canvas.LeftProperty, anim);
                }
            }));
        }

        private static void AudioDebug(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] [DanzFlyout Audio] {message}";
                Debug.WriteLine(line);
                Trace.WriteLine(line);
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DanzFlyout");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "audio-debug.txt"), line + Environment.NewLine);
            }
            catch { }
        }

        private void StartAudio()
        {
            AudioDebug("StartAudio requested.");
            Task.Run(async () =>
            {
                bool ok = await SafeInitAudioAsync();
                AudioDebug($"Initial audio initialization finished. Success={ok}");
            });
        }

        private MMDeviceEnumerator EnsureEnumerator()
        {
            lock (_enumLock)
            {
                if (_deviceEnumerator == null)
                {
                    var enumerator = new MMDeviceEnumerator();
                    enumerator.RegisterEndpointNotificationCallback(this);
                    _deviceEnumerator = enumerator;
                }

                return _deviceEnumerator;
            }
        }

        // Throw the enumerator away (used when a COM call got stuck) without blocking anything.
        private void ResetEnumeratorInBackground()
        {
            MMDeviceEnumerator? old;
            lock (_enumLock)
            {
                old = _deviceEnumerator;
                _deviceEnumerator = null;
            }

            if (old == null) return;

            var t = new Thread(() =>
            {
                try { old.UnregisterEndpointNotificationCallback(this); } catch { }
                try { old.Dispose(); } catch { }
            })
            { IsBackground = true, Name = "DanzFlyout.EnumDispose" };
            t.Start();
        }

        // StopRecording()/Dispose() can block for a long time while Windows switches endpoints.
        // They run on a throw-away thread so a stuck call can never freeze the visualizer again.
        private void DisposeCaptureInBackground(WasapiLoopbackCapture capture)
        {
            try { capture.DataAvailable -= OnAudioDataAvailable; } catch { }
            try { capture.RecordingStopped -= OnCaptureStopped; } catch { }

            var t = new Thread(() =>
            {
                try { capture.StopRecording(); } catch { }
                try { capture.Dispose(); } catch { }
                AudioDebug("Old capture disposed (background).");
            })
            { IsBackground = true, Name = "DanzFlyout.CaptureDispose" };
            t.Start();
        }

        private void DisposeExistingCapture()
        {
            WasapiLoopbackCapture? capture;
            lock (_captureSwapLock)
            {
                capture = _capture;
                _capture = null;
                _captureDeviceId = null;
            }

            if (capture != null)
                DisposeCaptureInBackground(capture);
        }

        private async Task<bool> SafeInitAudioAsync(bool force = false)
        {
            // Never wait forever on the lock.
            if (!await _audioInitLock.WaitAsync(ConnectTimeoutMs))
            {
                AudioDebug("SafeInitAudioAsync: lock wait timed out.");
                return false;
            }

            try
            {
                int gen = Interlocked.Increment(ref _audioGeneration);
                AudioDebug($"SafeInitAudioAsync: attempt gen={gen}, force={force}.");

                var work = Task.Run(() => ConnectCore(gen, force));
                var finished = await Task.WhenAny(work, Task.Delay(ConnectTimeoutMs));

                if (finished != work)
                {
                    // Windows is stuck mid-switch. Abandon this attempt: bumping the generation makes
                    // a late finisher throw its own capture away instead of publishing it.
                    Interlocked.Increment(ref _audioGeneration);
                    AudioDebug("SafeInitAudioAsync: attempt TIMED OUT, abandoning and resetting enumerator.");
                    ResetEnumeratorInBackground();
                    return false;
                }

                return await work;
            }
            catch (Exception ex)
            {
                AudioDebug($"SafeInitAudioAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                _audioInitLock.Release();
            }
        }

        // Runs on a pool thread. It may block; the caller enforces the timeout.
        private bool ConnectCore(int gen, bool force)
        {
            MMDevice? device = null;
            WasapiLoopbackCapture? capture = null;

            try
            {
                var enumerator = EnsureEnumerator();
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                if (device == null || device.State != DeviceState.Active)
                {
                    AudioDebug("ConnectCore: default endpoint missing or inactive.");
                    device?.Dispose();
                    return false;
                }

                string id = device.ID;

                // Already capturing from this exact device? Keep it. This stops unrelated device events
                // (microphones, other endpoints, repeated notifications) from tearing down a healthy capture.
                if (!force && _capture != null && id == _captureDeviceId)
                {
                    AudioDebug("ConnectCore: capture already on the default device, keeping it.");
                    device.Dispose();
                    return true;
                }

                AudioDebug($"ConnectCore: creating capture for {id}.");
                capture = new WasapiLoopbackCapture(device);
                capture.DataAvailable += OnAudioDataAvailable;
                capture.RecordingStopped += OnCaptureStopped;
                capture.StartRecording();

                WasapiLoopbackCapture? old = null;
                bool stale = false;

                lock (_captureSwapLock)
                {
                    if (gen != Volatile.Read(ref _audioGeneration))
                    {
                        stale = true;
                    }
                    else
                    {
                        old = _capture;
                        _capture = capture;
                        _captureDeviceId = id;
                        Interlocked.Exchange(ref _lastAudioDataTicks, Environment.TickCount64);
                    }
                }

                if (stale)
                {
                    AudioDebug("ConnectCore: attempt was abandoned while starting, discarding its capture.");
                    DisposeCaptureInBackground(capture);
                    return false;
                }

                if (old != null)
                    DisposeCaptureInBackground(old);

                AudioDebug($"ConnectCore: capture running, format={capture.WaveFormat}.");
                return true;
            }
            catch (Exception ex)
            {
                AudioDebug($"ConnectCore EXCEPTION: {ex.GetType().Name}: {ex.Message}");

                if (capture != null)
                    DisposeCaptureInBackground(capture);

                return false;
            }
        }

        private void OnCaptureStopped(object? sender, StoppedEventArgs e)
        {
            // Old/abandoned captures also raise this when they are shut down; only the live one matters.
            if (!ReferenceEquals(sender, Volatile.Read(ref _capture)))
                return;

            AudioDebug($"RecordingStopped on the CURRENT capture ({e.Exception?.GetType().Name ?? "no error"}), rebuilding.");
            Interlocked.Exchange(ref _forceReconnect, 1);
            ScheduleReconnect();
        }

        // Reads the default render endpoint id + its live peak level (what Windows is really playing).
        private (string Id, float Peak) ReadDefaultRenderState()
        {
            var enumerator = EnsureEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return (dev.ID, dev.AudioMeterInformation.MasterPeakValue);
        }

        private void AudioWatchdogLoop()
        {
            while (!_stopping)
            {
                Thread.Sleep(1000);

                // Diagnostic: is the UI thread still processing messages?
                try
                {
                    long tick = Environment.TickCount64;

                    if (Interlocked.Exchange(ref _uiPingPending, 1) == 0)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                        {
                            Interlocked.Exchange(ref _uiHeartbeatTicks, Environment.TickCount64);
                            Interlocked.Exchange(ref _uiPingPending, 0);
                        }));
                    }

                    long uiAge = tick - Interlocked.Read(ref _uiHeartbeatTicks);
                    if (uiAge > 3000 && tick - _lastUiWarn > 5000)
                    {
                        _lastUiWarn = tick;
                        AudioDebug($"UI thread unresponsive for {uiAge} ms.");
                    }
                }
                catch { }

                try
                {
                    if (_stopping || _isFullscreenBlocked) continue;
                    if (Volatile.Read(ref _isReconnecting) == 1) continue;

                    long now = Environment.TickCount64;
                    if (now - _lastWatchdogKick < 4000) continue;

                    // COM calls can stall during a device switch: give them a deadline, never wait forever.
                    var read = Task.Run(ReadDefaultRenderState);
                    if (!read.Wait(1500))
                    {
                        AudioDebug("Watchdog: reading the default device timed out (Windows is busy).");
                        continue;
                    }

                    var (id, peak) = read.Result;

                    // 1) Windows switched devices but we never got (or lost) the notification.
                    if (_captureDeviceId != null && id != _captureDeviceId)
                    {
                        _lastWatchdogKick = now;
                        AudioDebug($"Watchdog: default device is {id} but capturing {_captureDeviceId}, reconnecting.");
                        ScheduleReconnect();
                        continue;
                    }

                    // 2) Windows is playing sound on this device, but our capture only delivers empty buffers.
                    long idle = now - Interlocked.Read(ref _lastAudioDataTicks);
                    if (idle > 2000 && peak > 0.002f)
                    {
                        _lastWatchdogKick = now;
                        AudioDebug($"Watchdog: device peak={peak:0.000} but capture silent for {idle} ms, forcing rebuild.");
                        Interlocked.Exchange(ref _forceReconnect, 1);
                        ScheduleReconnect();
                    }
                }
                catch (Exception ex)
                {
                    AudioDebug($"Watchdog EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private void ScheduleReconnect()
        {
            if (_stopping) return;

            AudioDebug("ScheduleReconnect: requested.");
            Interlocked.Exchange(ref _reconnectRequested, 1);

            if (Interlocked.Exchange(ref _isReconnecting, 1) == 1)
                return;

            Task.Run(async () =>
            {
                try
                {
                    // Bluetooth/USB switches fire a burst of events; let them settle and coalesce.
                    await Task.Delay(250);

                    while (Volatile.Read(ref _reconnectRequested) == 1 && !_stopping)
                    {
                        Interlocked.Exchange(ref _reconnectRequested, 0);
                        bool force = Interlocked.Exchange(ref _forceReconnect, 0) == 1;
                        bool connected = false;

                        for (int attempt = 0; attempt < 12 && !_stopping; attempt++)
                        {
                            connected = await SafeInitAudioAsync(force);
                            AudioDebug($"Reconnect attempt {attempt + 1}/12 result={connected}.");

                            if (connected)
                                break;

                            await Task.Delay(Math.Min(100 * (attempt + 1), 1000));
                        }

                        if (!connected)
                        {
                            if (force) Interlocked.Exchange(ref _forceReconnect, 1);
                            Interlocked.Exchange(ref _reconnectRequested, 1);
                            await Task.Delay(1000);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isReconnecting, 0);

                    if (Volatile.Read(ref _reconnectRequested) == 1 && !_stopping)
                        ScheduleReconnect();
                }
            });
        }

        public void OnDefaultDeviceChanged(
    DataFlow flow,
    Role role,
    string defaultDeviceId)
{
    if (flow != DataFlow.Render)
        return;

    AudioDebug(
        $"OnDefaultDeviceChanged: flow={flow}, role={role}, id={defaultDeviceId}");

    ScheduleReconnect();
}

        public void OnDeviceStateChanged(
            string deviceId,
            DeviceState newState)
        {
            AudioDebug($"OnDeviceStateChanged: id={deviceId}, state={newState}");
            if (newState == DeviceState.Active ||
                newState == DeviceState.Unplugged ||
                newState == DeviceState.Disabled)
            {
                ScheduleReconnect();
            }
        }

        public void OnDeviceAdded(
            string pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(
            string deviceId)
        {
            AudioDebug($"OnDeviceRemoved: id={deviceId}");
            ScheduleReconnect();
        }

        public void OnPropertyValueChanged(
            string pwstrDeviceId,
            PropertyKey key)
        {
        }

      private void OnAudioDataAvailable(object? sender, WaveInEventArgs a)
{
    long debugCount = Interlocked.Increment(ref _audioDebugDataCount);

    if (debugCount == 1 || debugCount % 500 == 0)
    {
        AudioDebug(
            $"OnAudioDataAvailable: event #{debugCount}, bytes={a.BytesRecorded}");
    }

    if (_isFullscreenBlocked)
        return;

    if (a.BytesRecorded <= 0)
    {
        long empties = Interlocked.Increment(ref _emptyBufferCount);
        if (empties == 1 || empties % 500 == 0)
            AudioDebug($"OnAudioDataAvailable: empty buffer #{empties}");
        return;
    }

    long prevEmpties = Interlocked.Exchange(ref _emptyBufferCount, 0);
    if (prevEmpties > 0)
        AudioDebug($"OnAudioDataAvailable: audio data resumed after {prevEmpties} empty buffers.");

    // Use the capture that actually raised this event.
    // Do NOT read _capture here because it can change during
    // a playback-device transition.
    var capture = sender as WasapiLoopbackCapture;

    if (capture == null)
    {
        AudioDebug(
            "OnAudioDataAvailable: sender is not WasapiLoopbackCapture.");
        return;
    }

    // Ignore data from any capture that is no longer the live one (abandoned / being disposed).
    if (!ReferenceEquals(capture, Volatile.Read(ref _capture)))
        return;

    Interlocked.Exchange(ref _lastAudioDataTicks, Environment.TickCount64);

    int channels = capture.WaveFormat.Channels;

    if (channels <= 0)
    {
        AudioDebug(
            $"OnAudioDataAvailable: invalid channel count={channels}.");
        return;
    }

            lock (_fftLock)
            {
                try
                {
                    if (capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                    {
                        var buffer = new WaveBuffer(a.Buffer);
                        int sampleCount = a.BytesRecorded / 4;

                        for (int i = 0; i < sampleCount; i += channels)
                        {
                            float left = buffer.FloatBuffer[i];
                            float right = (channels > 1) ? buffer.FloatBuffer[i + 1] : left;

                            float window = 0.5f * (1f - MathF.Cos(2f * MathF.PI * _fftPos / (FftLength - 1)));
                            _fftBufferL[_fftPos].X = left * window;
                            _fftBufferL[_fftPos].Y = 0;
                            _fftBufferR[_fftPos].X = right * window;
                            _fftBufferR[_fftPos].Y = 0;
                            _fftPos++;

                            if (_fftPos >= 512)
                            {
                                ComputeFftMagnitudes();
                                Array.Copy(_fftBufferL, 512, _fftBufferL, 0, 512);
                                Array.Copy(_fftBufferR, 512, _fftBufferR, 0, 512);
                                _fftPos = 0;
                            }
                        }
                    }
                    else
                    {
                        int sampleCount = a.BytesRecorded / 2;
                        for (int i = 0; i < sampleCount; i += channels)
                        {
                            short sL = BitConverter.ToInt16(a.Buffer, i * 2);
                            short sR = (channels > 1) ? BitConverter.ToInt16(a.Buffer, (i + 1) * 2) : sL;

                            float window = 0.5f * (1f - MathF.Cos(2f * MathF.PI * _fftPos / (FftLength - 1)));
                            _fftBufferL[_fftPos].X = (sL / 32768f) * window;
                            _fftBufferL[_fftPos].Y = 0;
                            _fftBufferR[_fftPos].X = (sR / 32768f) * window;
                            _fftBufferR[_fftPos].Y = 0;
                            _fftPos++;

                            if (_fftPos >= 512)
                            {
                                ComputeFftMagnitudes();
                                Array.Copy(_fftBufferL, 512, _fftBufferL, 0, 512);
                                Array.Copy(_fftBufferR, 512, _fftBufferR, 0, 512);
                                _fftPos = 0;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void ComputeFftMagnitudes()
        {
            FastFourierTransform.FFT(true, FftM, _fftBufferL);
            FastFourierTransform.FFT(true, FftM, _fftBufferR);

            int half = FftLength / 2;
            for (int i = 0; i < half; i++)
            {
                float mL = MathF.Sqrt((_fftBufferL[i].X * _fftBufferL[i].X) + (_fftBufferL[i].Y * _fftBufferL[i].Y));
                float mR = MathF.Sqrt((_fftBufferR[i].X * _fftBufferR[i].X) + (_fftBufferR[i].Y * _fftBufferR[i].Y));
                _fftMagnitudes[i] = (mL + mR) * 0.5f;
            }

            // Publish only a completed FFT frame. The render thread never
            // reads _fftMagnitudes while the audio thread is modifying it.
            lock (_fftSnapshotLock)
            {
                Array.Copy(_fftMagnitudes, _fftPublishedSnapshot, _fftMagnitudes.Length);
            }
        }

        private static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);

        private static float BandEnergy(float[] fft, int minBin, int maxBin)
        {
            float sum = 0f;
            int count = Math.Max(1, maxBin - minBin + 1);
            for (int k = minBin; k <= maxBin && k < fft.Length; k++)
            {
                float x = fft[k];
                sum += x * x;
            }
            return MathF.Sqrt(sum / count);
        }

        // =========================================================================
        // EDM Transient-Driven Physics & Ballistics Engine
        // =========================================================================
        private void ProcessSpringPhysics(float dt)
        {
            int totalBars = _barHeight.Length;
            if (totalBars <= 0) return;

            // Kick / bass energy
            float kick = BandEnergy(_fftRenderBuffer, 1, 5);

            // Snare / percussion energy
            float snare = BandEnergy(_fftRenderBuffer, 43, 128);

            // Positive spectral flux (onset differentials)
            float kickDelta = MathF.Max(0f, kick - _prevSub);
            float snareDelta = MathF.Max(0f, snare - _prevHigh);
            _prevSub = kick;
            _prevHigh = snare;

            // Adaptive reference floors
            _subRef *= MathF.Exp(-dt * 0.35f);
            _highRef *= MathF.Exp(-dt * 0.35f);
            _subRef = MathF.Max(_subRef, kick);
            _highRef = MathF.Max(_highRef, snare);

            float kickHit = Clamp01(kickDelta / (_subRef * 0.08f + 1e-6f));
            float snareHit = Clamp01(snareDelta / (_highRef * 0.06f + 1e-6f));

            if (kickHit < 0.10f) kickHit = 0f;
            if (snareHit < 0.10f) snareHit = 0f;

            kickHit = MathF.Pow(kickHit, 0.55f);
            snareHit = MathF.Pow(snareHit, 0.50f);

            _kickEnvelope = MathF.Max(kickHit, _kickEnvelope * MathF.Exp(-dt * 9.0f));
            _snareEnvelope = MathF.Max(snareHit, _snareEnvelope * MathF.Exp(-dt * 11.0f));

            int center = totalBars / 2;

            float framePeak = 0f;
            for (int i = 0; i < totalBars; i++)
            {
                float t = (float)Math.Abs(i - center) / Math.Max(1, center);
                int bin = Math.Clamp(2 + (int)(t * 126), 0, _fftRenderBuffer.Length - 1);

                float localSum = 0f;
                int localCount = 0;
                for (int k = Math.Max(0, bin - 1); k <= Math.Min(_fftRenderBuffer.Length - 1, bin + 1); k++)
                {
                    localSum += _fftRenderBuffer[k] * _fftRenderBuffer[k];
                    localCount++;
                }

                float local = MathF.Sqrt(localSum / Math.Max(1, localCount));
                framePeak = MathF.Max(framePeak, local);
            }

            float peakAttack = 1f - MathF.Exp(-dt * 7.0f);
            float peakRelease = 1f - MathF.Exp(-dt * 0.75f);
            if (framePeak > _visualPeakRef)
                _visualPeakRef += (framePeak - _visualPeakRef) * peakAttack;
            else
                _visualPeakRef += (framePeak - _visualPeakRef) * peakRelease;

            _visualPeakRef = MathF.Max(_visualPeakRef, 1e-5f);

            for (int i = 0; i < totalBars; i++)
            {
                int dist = Math.Abs(i - center);
                float t = (float)dist / Math.Max(1, center);

                float bassRegion = MathF.Max(0f, 1f - (t / 0.65f));
                float highRegion = MathF.Max(0f, (t - 0.35f) / 0.65f);
                float bassWeight = MathF.Pow(bassRegion, 2.0f);
                float highWeight = MathF.Pow(highRegion, 2.0f);

                float groove = (_kickEnvelope * bassWeight * 1.15f)
                             + (_snareEnvelope * highWeight * 1.05f);

                int bin = Math.Clamp(2 + (int)(t * 126), 0, _fftRenderBuffer.Length - 1);

                float localSum = 0f;
                int localCount = 0;
                for (int k = Math.Max(0, bin - 1); k <= Math.Min(_fftRenderBuffer.Length - 1, bin + 1); k++)
                {
                    localSum += _fftRenderBuffer[k] * _fftRenderBuffer[k];
                    localCount++;
                }

                float local = MathF.Sqrt(localSum / Math.Max(1, localCount));

                float reference = _barReference[i];
                if (reference < 1e-5f)
                    reference = local;

                float refAttack = 1f - MathF.Exp(-dt * 2.2f);
                float refRelease = 1f - MathF.Exp(-dt * 0.35f);
                if (local > reference)
                    reference += (local - reference) * refAttack;
                else
                    reference += (local - reference) * refRelease;

                reference = MathF.Max(reference, 1e-5f);
                _barReference[i] = reference;

                float loudness = Clamp01(local / reference);
                float shaped = MathF.Pow(loudness, 1.15f);

                float globalLevel = Clamp01(local / _visualPeakRef);
                float target = (shaped * 0.76f) + (globalLevel * 0.06f) + (groove * 0.18f);
                target = Clamp01(target);

                float current = _barHeight[i];

                if (target > current)
                {
                    float rise = target - current;
                    if (rise > 0.025f)
                        current = target;
                }
                else
                {
                    float drop = current - target;
                    if (drop < 0.06f)
                    {
                        current = target;
                    }
                    else
                    {
                        float release = 1f - MathF.Exp(-dt * 18.0f);
                        current += (target - current) * release;
                    }
                }

                _barHeight[i] = Clamp01(current);
                _barVelocity[i] = 0f;
            }
        }

        private void RenderFrame(object? sender, EventArgs e)
        {
            if (!_isVisible || _isFullscreenBlocked) return;

            var panel = FindName("BarsPanel") as StackPanel;
            var capsCanvas = FindName("PeakCapsCanvas") as Canvas;
            var coverScale = FindName("AlbumCoverScale") as ScaleTransform;
            if (panel == null || capsCanvas == null) return;

            int count = panel.Children.Count;

            DateTime now = DateTime.UtcNow;
            float dt = Math.Clamp((float)(now - _lastFrameTime).TotalSeconds, 0.001f, 0.033f);
            _lastFrameTime = now;

            // Take the latest completed FFT frame in a tiny lock, then
            // process it completely outside the lock. This prevents the
            // UI/render thread from ever waiting on audio FFT processing.
            lock (_fftSnapshotLock)
            {
                Array.Copy(_fftPublishedSnapshot, _fftRenderBuffer, _fftRenderBuffer.Length);
            }

            ProcessSpringPhysics(dt);

            double sensitivity = SettingsConfig.Current.AudioSensitivity;
            const double maxBarHeight = 22.0;
            const double minBarHeight = 3.0;

            // Tactile Subwoofer Kick Drum pop on album art
            double targetScale = 1.0;
            double targetGlowBlur = 5.0;

            if (_kickEnvelope > 0.40f)
            {
                double punch = (_kickEnvelope - 0.40f) / 0.60f;
                targetScale = 1.0 + (punch * 0.13);
                targetGlowBlur = 5.0 + (punch * 7.0);
            }

            if (targetScale > _bassPumpScale)
                _bassPumpScale = targetScale;
            else
                _bassPumpScale = (_bassPumpScale * 0.60) + (1.0 * 0.40);

            if (targetGlowBlur > _currentGlowBlur)
                _currentGlowBlur = targetGlowBlur;
            else
                _currentGlowBlur = (_currentGlowBlur * 0.65) + (5.0 * 0.35);

            if (coverScale != null)
            {
                coverScale.ScaleX = _bassPumpScale;
                coverScale.ScaleY = _bassPumpScale;
            }

            bool isDynamic = SettingsConfig.Current.VisualizerTheme == "Dynamic";
            if (isDynamic) _colorPhase += 0.035;

            bool isPeakCapsMode = SettingsConfig.Current.VisualizerMode == "PeakCaps";

            if (SettingsConfig.Current.VisualizerStyle == "FluidWave")
            {
                RenderFluidWave(dt, sensitivity, isDynamic);
                return;
            }

            if (count != _barHeight.Length) return;

            for (int i = 0; i < count; i++)
            {
                if (panel.Children[i] is Border bar)
                {
                    float currentSpringHeight = _barHeight[i];
                    double targetHeight = minBarHeight + (currentSpringHeight * sensitivity * (maxBarHeight - minBarHeight));
                    targetHeight = Math.Clamp(targetHeight, minBarHeight, maxBarHeight * 1.2);

                    bar.Height = targetHeight;

                    // Do not change DropShadowEffect.BlurRadius every frame.
                    // WPF can re-render/recompose each affected element when an Effect
                    // property changes, which can cause intermittent UI-thread stalls.
                    // Keep the existing glow effect static during the high-frequency
                    // visualizer render loop.

                    double targetOpacity = (currentSpringHeight > 0.03f) ? 1.0 : 0.0;
                    double currOpacity = _barOpacities[i];

                    if (targetOpacity > currOpacity)
                        currOpacity = 1.0;
                    else
                        currOpacity = Math.Max(0.0, currOpacity - 0.25);

                    _barOpacities[i] = currOpacity;
                    bar.Opacity = currOpacity;

                    if (capsCanvas.Children.Count > i && capsCanvas.Children[i] is Border cap)
                    {
                        if (isPeakCapsMode && currOpacity > 0.0)
                        {
                            if (targetHeight >= _peakCapHeights[i])
                            {
                                _peakCapHeights[i] = targetHeight;
                                _peakCapVelocities[i] = 0;
                                _peakCapHold[i] = 3;
                            }
                            else
                            {
                                if (_peakCapHold[i] > 0)
                                {
                                    _peakCapHold[i]--;
                                }
                                else
                                {
                                    _peakCapVelocities[i] += 0.45;
                                    _peakCapHeights[i] = Math.Max(minBarHeight, _peakCapHeights[i] - _peakCapVelocities[i]);
                                }
                            }

                            cap.Opacity = currOpacity;
                            Canvas.SetBottom(cap, _peakCapHeights[i] + 1.2);
                        }
                        else
                        {
                            cap.Opacity = 0.0;
                        }
                    }

                    if (isDynamic && currOpacity > 0.0)
                    {
                        double t = (Math.Sin(_colorPhase + (i * 0.14)) + 1.0) / 2.0;
                        byte r = (byte)(_colorA.R + (_colorB.R - _colorA.R) * t);
                        byte g = (byte)(_colorA.G + (_colorB.G - _colorA.G) * t);
                        byte b = (byte)(_colorA.B + (_colorB.B - _colorA.B) * t);

                        var dynamicColor = Color.FromRgb(r, g, b);
                        if (bar.Background is SolidColorBrush dynamicBrush)
                        {
                            dynamicBrush.Color = dynamicColor;
                        }
                        else
                        {
                            bar.Background = new SolidColorBrush(dynamicColor);
                        }
                        if (bar.Effect is DropShadowEffect barGlow) barGlow.Color = dynamicColor;
                    }
                }
            }
        }

        private void RenderFluidWave(float dt, double sensitivity, bool isDynamic)
        {
            if (_fluidWavePaths.Count == 0) return;

            var canvas = FindName("FluidWaveCanvas") as Canvas;
            if (canvas == null) return;

            double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : TotalVisualizerWidth;
            double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 54.0;
            double centerY = height * 0.5;

            _fluidWavePhase += dt * (1.80 + (_kickEnvelope * 2.15) + (_snareEnvelope * 0.45));

            int layerCount = _fluidWavePaths.Count;
            const int sampleCount = 34;

            for (int layer = 0; layer < layerCount; layer++)
            {
                var path = _fluidWavePaths[layer];
                double layerT = layerCount <= 1 ? 0.0 : (double)layer / (layerCount - 1);
                double centerWeight = 1.0 - Math.Abs((layerT * 2.0) - 1.0);

                float spring = layer < _barHeight.Length ? _barHeight[layer] : 0f;
                double beatBoost = Math.Clamp((_kickEnvelope - 0.08) * 2.2, 0.0, 1.0);
                double amplitude = (2.4 + (spring * 17.0 * sensitivity)) * (0.55 + (centerWeight * 0.45));
                amplitude += beatBoost * (5.0 + centerWeight * 8.0);
                amplitude += _snareEnvelope * (1.5 + centerWeight * 2.5);

                double phase = _fluidWavePhase + (layer * 0.17);
                double frequency = 1.05 + (centerWeight * 0.75) + (layerT * 0.12);

                var figure = new PathFigure
                {
                    IsClosed = false,
                    IsFilled = false,
                    StartPoint = new Point(0, centerY)
                };

               var points = new PointCollection();
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    double t = (double)sample / (sampleCount - 1);
                    double x = t * (width - 1.0);

                    int bin = Math.Clamp(2 + (int)(t * 150.0), 0, _fftRenderBuffer.Length - 1);
                    float spectral = _fftRenderBuffer[bin];
                    double normalized = Clamp01(spectral / Math.Max(_visualPeakRef, 1e-5f));
                    double smoothEnergy = 0.24 + (normalized * 1.55);

                    // A broad sine plus a finer harmonic gives the layered waveform shape
                    double primary = Math.Sin((t * Math.PI * 2.0 * frequency) + phase);
                    double harmonic = Math.Sin((t * Math.PI * 2.0 * (frequency * 2.35)) - phase * 0.72);
                    double envelope = Math.Sin(Math.PI * t);
                    
                    double rawYOffset = (primary * 0.72 + harmonic * 0.28)
                                       * amplitude * smoothEnergy * (0.25 + (0.75 * envelope));

                    // Soft compression: smooth out excess height so peaks stay within taskbar bounds
                    double maxRoom = Math.Max(2.0, centerY - 2.0);
                    double threshold = maxRoom * 0.65;
                    double absVal = Math.Abs(rawYOffset);

                    double yOffset;
                    if (absVal <= threshold)
                    {
                        yOffset = rawYOffset;
                    }
                    else
                    {
                        double excess = absVal - threshold;
                        double headroom = maxRoom - threshold;
                        double compressed = threshold + (headroom * Math.Tanh(excess / headroom));
                        yOffset = Math.Sign(rawYOffset) * compressed;
                    }

                    points.Add(new Point(x, centerY - yOffset));
                }

                figure.Segments.Add(new PolyLineSegment(points, true));
                path.Data = new PathGeometry(new[] { figure });

                double targetOpacity = 0.16 + (centerWeight * 0.84);
                path.Opacity = targetOpacity;

                if (isDynamic)
                {
                    double colorT = (Math.Sin(_colorPhase + layerT * Math.PI) + 1.0) / 2.0;
                    byte r = (byte)(_colorA.R + (_colorB.R - _colorA.R) * colorT);
                    byte g = (byte)(_colorA.G + (_colorB.G - _colorA.G) * colorT);
                    byte b = (byte)(_colorA.B + (_colorB.B - _colorA.B) * colorT);
                    var dynamicColor = Color.FromRgb(r, g, b);
                    path.Stroke = new SolidColorBrush(dynamicColor);
                    if (path.Effect is DropShadowEffect glow) glow.Color = dynamicColor;
                }
            }
        }
        public void SetVisualizerColor(Color color)
        {
            if (SettingsConfig.Current.VisualizerStyle == "FluidWave")
            {
                foreach (var path in _fluidWavePaths)
                {
                    path.Stroke = new SolidColorBrush(color);
                    if (path.Effect is DropShadowEffect glow) glow.Color = color;
                }
                return;
            }

            var panel = FindName("BarsPanel") as StackPanel;
            if (panel == null) return;

            var brush = new SolidColorBrush(color);
            foreach (var child in panel.Children)
            {
                if (child is Border bar)
                {
                    bar.Background = brush;
                    if (bar.Effect is DropShadowEffect glow) glow.Color = color;
                }
            }
        }

        private void StopAudio()
        {
            _stopping = true;

            Task.Run(() =>
            {
                try
                {
                    if (_audioWatchdogTimer != null)
                    {
                        _audioWatchdogTimer.Stop();
                        _audioWatchdogTimer = null;
                    }

                    if (_fullscreenCheckTimer != null)
                    {
                        _fullscreenCheckTimer.Stop();
                        _fullscreenCheckTimer = null;
                    }

                    if (_nativeFlyoutTimer != null)
                    {
                        _nativeFlyoutTimer.Stop();
                        _nativeFlyoutTimer = null;
                    }

                    if (_deviceEnumerator != null)
                    {
                        _deviceEnumerator.UnregisterEndpointNotificationCallback(this);
                        _deviceEnumerator.Dispose();
                        _deviceEnumerator = null;
                    }

                    DisposeExistingCapture();
                }
                catch { }
            });
        }
    }
}