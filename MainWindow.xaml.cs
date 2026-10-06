using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace DanzFlyout
{
    public partial class MainWindow : Window
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private DispatcherTimer? _hideTimer;
        private bool _isMouseOver = false;
        private string _lastTrackId = string.Empty;
        private bool _isCardVisible = false;

        public bool UseAdaptiveAlbumColor { get; set; } = true;

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

        public MainWindow()
        {
            InitializeComponent();
            SetupHideTimer();
            Loaded += MainWindow_Loaded;
            SizeChanged += (s, e) => ApplyPlacementSettings();
        }

        private void SetupHideTimer()
        {
            double durationSec = SettingsConfig.Current?.MediaFlyoutDuration ?? 3.8;
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(1.0, durationSec))
            };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                if (!_isMouseOver)
                {
                    AnimateOut();
                }
            };
        }

        public void UpdateDurationFromSettings()
        {
            if (_hideTimer != null)
            {
                double durationSec = SettingsConfig.Current?.MediaFlyoutDuration ?? 3.8;
                _hideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1.0, durationSec));
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            // Eliminate rectangular DWM desktop shadow bleeding
            int renderingPolicy = DWMNCRP_DISABLED;
            DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref renderingPolicy, sizeof(int));

            WindowBackdropHelper.ApplyAcrylic(hwnd, darkMode: true);
            ApplyPlacementSettings();
        }

        public void ApplyPlacementSettings()
        {
            try
            {
                var screens = Screen.AllScreens;
                if (screens.Length == 0) return;

                int targetIndex = Math.Clamp(SettingsConfig.Current.MediaFlyoutMonitorIndex, 0, screens.Length - 1);
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
                if (double.IsNaN(w) || w <= 0) w = 315;
                if (double.IsNaN(h) || h <= 0) h = 96;

                const double margin = 20.0;

                switch (SettingsConfig.Current.MediaFlyoutPlacement)
                {
                    case 1: Left = workLeft + margin; Top = workBottom - h - margin; break;
                    case 2: Left = workRight - w - margin; Top = workTop + margin; break;
                    case 3: Left = workLeft + margin; Top = workTop + margin; break;
                    case 4: Left = workLeft + (workWidth - w) / 2.0; Top = workTop + margin; break;
                    case 5: Left = workLeft + (workWidth - w) / 2.0; Top = workBottom - h - margin; break;
                    case 6: Left = workLeft + margin; Top = workTop + (workHeight - h) / 2.0; break;
                    case 7: Left = workRight - w - margin; Top = workTop + (workHeight - h) / 2.0; break;
                    default: Left = workRight - w - margin; Top = workBottom - h - margin; break;
                }
            }
            catch
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - (ActualWidth > 0 ? ActualWidth : 315) - 20;
                Top = workArea.Bottom - (ActualHeight > 0 ? ActualHeight : 96) - 20;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += (s, args) => Dispatcher.Invoke(SyncSession);
                    _sessionManager.SessionsChanged += (s, args) => Dispatcher.Invoke(SyncSession);
                    SyncSession();
                }
            }
            catch (Exception ex)
            {
                var titleBlock = FindName("TrackTitle") as TextBlock;
                var artistBlock = FindName("TrackArtist") as TextBlock;
                if (titleBlock != null) titleBlock.Text = "SMTC Error";
                if (artistBlock != null) artistBlock.Text = ex.Message;
            }
        }

        private void SyncSession()
        {
            if (_sessionManager == null) return;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            }

            var sessions = _sessionManager.GetSessions();
            GlobalSystemMediaTransportControlsSession? bestSession = null;

            foreach (var session in sessions)
            {
                string appId = session.SourceAppUserModelId.ToLowerInvariant();
                bool isMusicApp = appId.Contains("spotify") || appId.Contains("applemusic") ||
                                  appId.Contains("tidal") || appId.Contains("deezer");

                if (isMusicApp)
                {
                    var playbackInfo = session.GetPlaybackInfo();
                    if (playbackInfo != null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        bestSession = session;
                        break;
                    }
                }
            }

            _currentSession = bestSession ?? _sessionManager.GetCurrentSession();

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                RefreshMediaInfo(showFlyout: true);
                UpdatePlaybackState();
            }
            else
            {
                App.VisualizerInstance?.UpdateMedia(string.Empty, null, false);
            }
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            Dispatcher.Invoke(() => RefreshMediaInfo(showFlyout: true));
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
{
    Dispatcher.Invoke(() =>
    {
        SyncSession();
        UpdatePlaybackState();
        SyncVisualizerVisibility();
        // Do NOT call ShowAndTriggerTimer() here
    });
}

        private void SyncVisualizerVisibility()
        {
            if (_currentSession == null)
            {
                App.VisualizerInstance?.UpdateMedia(string.Empty, null, false);
                return;
            }

            var info = _currentSession.GetPlaybackInfo();
            bool isPlaying = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var titleBlock = FindName("TrackTitle") as TextBlock;
            var albumCover = FindName("AlbumCover") as Image;

            App.VisualizerInstance?.UpdateMedia(
                titleBlock?.Text ?? string.Empty,
                albumCover?.Source as BitmapSource,
                isPlaying
            );
        }

        private void UpdatePlaybackState()
        {
            if (_currentSession == null) return;
            try
            {
                var playPauseIcon = FindName("PlayPauseIcon") as System.Windows.Shapes.Path;
                var btnPrev = FindName("BtnPrev") as Button;
                var btnNext = FindName("BtnNext") as Button;

                var info = _currentSession.GetPlaybackInfo();
                if (info != null)
                {
                    bool isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    if (playPauseIcon != null)
                    {
                        playPauseIcon.Data = Geometry.Parse(isPlaying
                            ? "M 3,3 L 6,3 L 6,15 L 3,15 Z M 10,3 L 13,3 L 13,15 L 10,15 Z"
                            : "M 4,4 L 14,9 L 4,14 Z");
                    }

                    var controls = info.Controls;
                    if (btnPrev != null && controls != null)
                    {
                        btnPrev.IsEnabled = controls.IsPreviousEnabled;
                        btnPrev.Opacity = controls.IsPreviousEnabled ? 1.0 : 0.35;
                    }
                    if (btnNext != null && controls != null)
                    {
                        btnNext.IsEnabled = controls.IsNextEnabled;
                        btnNext.Opacity = controls.IsNextEnabled ? 1.0 : 0.35;
                    }
                }
            }
            catch { }
        }

        private async void RefreshMediaInfo(bool showFlyout = false)
        {
            if (_currentSession == null) return;

            try
            {
                var props = await _currentSession.TryGetMediaPropertiesAsync();
                var info = _currentSession.GetPlaybackInfo();
                bool isPlaying = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                if (props != null)
                {
                    string newTrackId = $"{props.Title}_{props.Artist}";
                    bool isNewSong = newTrackId != _lastTrackId;
                    _lastTrackId = newTrackId;

                    string title = string.IsNullOrWhiteSpace(props.Title) ? "Unknown Title" : props.Title;
                    string artist = string.IsNullOrWhiteSpace(props.Artist) ? "Unknown Artist" : props.Artist;

                    BitmapImage? img = null;
                    if (props.Thumbnail != null)
                    {
                        using IRandomAccessStreamWithContentType stream = await props.Thumbnail.OpenReadAsync();
                        img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = stream.AsStreamForRead();
                        img.EndInit();
                        img.Freeze();
                    }

                    Dispatcher.Invoke(() =>
                    {
                        if (_isCardVisible && isNewSong)
                        {
                            ExecuteMeltingTransition(() =>
                            {
                                ApplyTrackContent(title, artist, img, isPlaying);
                            });
                        }
                        else
                        {
                            ApplyTrackContent(title, artist, img, isPlaying);
                        }

                        UpdatePlaybackState();

                        if (showFlyout && isNewSong)
{
    ShowAndTriggerTimer();
}
                    });
                }
            }
            catch { }
        }

        private void ApplyTrackContent(string title, string artist, BitmapImage? img, bool isPlaying)
        {
            var titleBlock = FindName("TrackTitle") as TextBlock;
            var artistBlock = FindName("TrackArtist") as TextBlock;
            var titleCanvas = FindName("TitleCanvas") as Canvas;
            var artistCanvas = FindName("ArtistCanvas") as Canvas;
            var albumCover = FindName("AlbumCover") as Image;

            if (titleBlock != null) titleBlock.Text = title;
            if (artistBlock != null) artistBlock.Text = artist;

            if (titleBlock != null && titleCanvas != null) SetupMarquee(titleBlock, titleCanvas);
            if (artistBlock != null && artistCanvas != null) SetupMarquee(artistBlock, artistCanvas);

            if (img != null && albumCover != null)
            {
                albumCover.Source = img;
                if (UseAdaptiveAlbumColor) ApplyAdaptiveGlass(img);
                App.VisualizerInstance?.UpdateMedia(title, img, isPlaying);
            }
            else
            {
                if (albumCover != null) albumCover.Source = null;
                ResetGlassColor();
                App.VisualizerInstance?.UpdateMedia(title, null, isPlaying);
            }
        }

        private void ExecuteMeltingTransition(Action onMidpointSwap)
        {
            var albumCover = FindName("AlbumCover") as Image;
            var albumScale = FindName("AlbumCoverTrackScale") as ScaleTransform;
            var title = FindName("TrackTitle") as TextBlock;
            var titleTranslate = FindName("TitleTrackTranslate") as TranslateTransform;
            var artist = FindName("TrackArtist") as TextBlock;
            var artistTranslate = FindName("ArtistTranslate") as TranslateTransform;

            // If the per-element animation targets are unavailable, keep the
            // track change functional rather than blocking the media update.
            if (albumCover == null || albumScale == null ||
                title == null || titleTranslate == null ||
                artist == null || artistTranslate == null)
            {
                onMidpointSwap();
                return;
            }

            var exitDuration = TimeSpan.FromMilliseconds(145);
            var enterDuration = TimeSpan.FromMilliseconds(230);
            bool swapped = false;

            // Album artwork: subtle shrink + fade.
            var albumFadeOut = new DoubleAnimation(0.0, exitDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var albumScaleOut = new DoubleAnimation(0.88, exitDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            // Title: vertical slide, deliberately different from the artist.
            var titleFadeOut = new DoubleAnimation(0.0, exitDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var titleSlideOut = new DoubleAnimation(-9.0, exitDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            // Artist: horizontal drift, smaller and softer than the title.
            var artistFadeOut = new DoubleAnimation(0.0, exitDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var artistSlideOut = new DoubleAnimation(13.0, exitDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            titleFadeOut.Completed += (s, e) =>
            {
                if (swapped) return;
                swapped = true;

                onMidpointSwap();

                // Reset all three elements for their individual entrances.
                albumCover.Opacity = 0.0;
                albumScale.ScaleX = 0.88;
                albumScale.ScaleY = 0.88;

                title.Opacity = 0.0;
                titleTranslate.X = 0.0;
                titleTranslate.Y = 10.0;

                artist.Opacity = 0.0;
                artistTranslate.X = -11.0;
                artistTranslate.Y = 0.0;

                var albumFadeIn = new DoubleAnimation(1.0, enterDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var albumScaleIn = new DoubleAnimation(1.0, enterDuration)
                {
                    EasingFunction = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut }
                };

                var titleFadeIn = new DoubleAnimation(1.0, enterDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var titleSlideIn = new DoubleAnimation(0.0, enterDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var artistFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(205))
                {
                    BeginTime = TimeSpan.FromMilliseconds(35),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var artistSlideIn = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(205))
                {
                    BeginTime = TimeSpan.FromMilliseconds(35),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                albumCover.BeginAnimation(OpacityProperty, albumFadeIn);
                albumScale.BeginAnimation(ScaleTransform.ScaleXProperty, albumScaleIn);
                albumScale.BeginAnimation(ScaleTransform.ScaleYProperty, albumScaleIn.Clone());

                title.BeginAnimation(OpacityProperty, titleFadeIn);
                titleTranslate.BeginAnimation(TranslateTransform.YProperty, titleSlideIn);

                artist.BeginAnimation(OpacityProperty, artistFadeIn);
                artistTranslate.BeginAnimation(TranslateTransform.XProperty, artistSlideIn);
            };

            albumCover.BeginAnimation(OpacityProperty, albumFadeOut);
            albumScale.BeginAnimation(ScaleTransform.ScaleXProperty, albumScaleOut);
            albumScale.BeginAnimation(ScaleTransform.ScaleYProperty, albumScaleOut.Clone());

            title.BeginAnimation(OpacityProperty, titleFadeOut);
            titleTranslate.BeginAnimation(TranslateTransform.YProperty, titleSlideOut);

            artist.BeginAnimation(OpacityProperty, artistFadeOut);
            artistTranslate.BeginAnimation(TranslateTransform.XProperty, artistSlideOut);
        }

        private void SetupMarquee(TextBlock textBlock, Canvas parentCanvas)
        {
            textBlock.BeginAnimation(Canvas.LeftProperty, null);
            Canvas.SetLeft(textBlock, 0);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                double textWidth = textBlock.ActualWidth;
                double canvasWidth = parentCanvas.ActualWidth;

                if (textWidth > canvasWidth && canvasWidth > 0)
                {
                    double diff = textWidth - canvasWidth;
                    var anim = new DoubleAnimation
                    {
                        From = 0,
                        To = -diff - 12,
                        Duration = TimeSpan.FromSeconds(Math.Max(3.0, diff / 25)),
                        BeginTime = TimeSpan.FromSeconds(1.2),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    textBlock.BeginAnimation(Canvas.LeftProperty, anim);
                }
            }));
        }

        private void ApplyAdaptiveGlass(BitmapSource bitmap)
        {
            var overlay = FindName("AccentOverlayBorder") as Border;
            if (overlay == null) return;

            Color accent = AccentExtractor.ExtractDominantColor(bitmap);

            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(85, accent.R, accent.G, accent.B), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(25, (byte)(accent.R / 2), (byte)(accent.G / 2), (byte)(accent.B / 2)), 1.0));

            overlay.Background = brush;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300));
            overlay.BeginAnimation(OpacityProperty, fadeIn);

            App.VisualizerInstance?.SetVisualizerColor(accent);
        }

        private void ResetGlassColor()
        {
            var overlay = FindName("AccentOverlayBorder") as Border;
            if (overlay == null) return;

            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200));
            overlay.BeginAnimation(OpacityProperty, fadeOut);
        }

        #region Media Controls Click Handlers

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            _hideTimer?.Stop();
            _hideTimer?.Start();
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
            }
        }

        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            _hideTimer?.Stop();
            _hideTimer?.Start();
            if (_currentSession != null)
            {
                await _currentSession.TryTogglePlayPauseAsync();
            }
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _hideTimer?.Stop();
            _hideTimer?.Start();
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
            }
        }

        #endregion

        #region Animations & Mouse Events

       public void ShowAndTriggerTimer()
        {
            if (FullscreenHelper.IsGameOrFullscreenActive())
            {
                return; // Do not show media flyout while in-game or fullscreen
            }

            AnimateIn();
            UpdateDurationFromSettings();
            _hideTimer?.Stop();
            _hideTimer?.Start();
        }
        private void AnimateIn()
{
    _isCardVisible = true;
    ApplyPlacementSettings();

    try
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
    catch { }

    var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(240))
    {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
    };

    var slideUp = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(280))
    {
        EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
    };

    BeginAnimation(OpacityProperty, fadeIn);
    CardTranslate.BeginAnimation(TranslateTransform.YProperty, slideUp);
}
        private void AnimateOut()
        {
            _isCardVisible = false;
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var slideDown = new DoubleAnimation(15.0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            BeginAnimation(OpacityProperty, fadeOut);
            CardTranslate.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _isMouseOver = true;
            _hideTimer?.Stop();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _isMouseOver = false;
            _hideTimer?.Start();
        }

        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Application.Current.Shutdown();
        }

        #endregion
    }
}
