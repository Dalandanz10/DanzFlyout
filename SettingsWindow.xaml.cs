using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace DanzFlyout
{
    public partial class SettingsWindow : Window
    {
        private bool _isLoaded = false;
        private bool _isSwitchingTabs = false;

        public SettingsWindow()
        {
            InitializeComponent();

            try
            {
                this.Icon = DanzFlyout.TrayIconService.GetWindowIconSource();
            }
            catch (Exception)
            {
            }

            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;

            try
            {
                if (ToggleVisPill != null) UpdateToggleVisual(ToggleVisPill, SettingsConfig.Current.EnableTaskbarVisualizer);

                if (CmbVisStyle != null)
                {
                    CmbVisStyle.SelectedIndex = SettingsConfig.Current.VisualizerStyle switch
                    {
                        "BottomBars" => 1,
                        "FluidWave" => 2,
                        _ => 0
                    };
                }

                if (CmbVisMonitor != null)
                {
                    CmbVisMonitor.SelectedIndex = Math.Clamp(SettingsConfig.Current.VisualizerMonitorIndex, 0, 1);
                }

                // Interactive Media Flyout Controls
                if (CmbMediaMonitor != null)
                {
                    CmbMediaMonitor.SelectedIndex = Math.Clamp(SettingsConfig.Current.MediaFlyoutMonitorIndex, 0, 1);
                }

                if (CmbPlacement != null)
                {
                    CmbPlacement.SelectedIndex = Math.Clamp(SettingsConfig.Current.MediaFlyoutPlacement, 0, 7);
                }

                if (SliderMediaDuration != null)
                {
                    SliderMediaDuration.Value = Math.Clamp(SettingsConfig.Current.MediaFlyoutDuration, 1.0, 10.0);
                }
                if (TxtMediaDuration != null)
                {
                    TxtMediaDuration.Text = $"{SettingsConfig.Current.MediaFlyoutDuration:0.0}s";
                }

                if (CmbVolMonitor != null)
                {
                    CmbVolMonitor.SelectedIndex = Math.Clamp(SettingsConfig.Current.VolumeFlyoutMonitorIndex, 0, 1);
                }

                if (CmbVolPlacement != null)
                {
                    CmbVolPlacement.SelectedIndex = Math.Clamp(SettingsConfig.Current.VolumeFlyoutPlacement, 0, 7);
                }

                if (CmbLockMonitor != null)
                {
                    CmbLockMonitor.SelectedIndex = Math.Clamp(SettingsConfig.Current.LockKeysMonitorIndex, 0, 1);
                }

                if (CmbLockPlacement != null)
                {
                    CmbLockPlacement.SelectedIndex = Math.Clamp(SettingsConfig.Current.LockKeysPlacement, 0, 7);
                }

                if (SliderVisOffset != null) SliderVisOffset.Value = SettingsConfig.Current.VisualizerOffset;
                if (TxtVisOffset != null) TxtVisOffset.Text = $"{(int)SettingsConfig.Current.VisualizerOffset}px";

                if (SliderBarCount != null) SliderBarCount.Value = SettingsConfig.Current.VisualizerBarCount;
                if (TxtBarCount != null) TxtBarCount.Text = $"{SettingsConfig.Current.VisualizerBarCount} bars";

                if (SliderSensitivity != null) SliderSensitivity.Value = SettingsConfig.Current.AudioSensitivity;
                if (TxtSensitivity != null) TxtSensitivity.Text = $"{SettingsConfig.Current.AudioSensitivity:0.0}x";

                if (SliderSpacing != null) SliderSpacing.Value = SettingsConfig.Current.VisualizerTitleSpacing;
                if (TxtSpacing != null) TxtSpacing.Text = $"{(int)SettingsConfig.Current.VisualizerTitleSpacing}px";

                if (TxtCustomHex != null) TxtCustomHex.Text = SettingsConfig.Current.CustomHexColor;
                if (TxtDynColorA != null) TxtDynColorA.Text = SettingsConfig.Current.DynamicColorA;
                if (TxtDynColorB != null) TxtDynColorB.Text = SettingsConfig.Current.DynamicColorB;

                if (ToggleVolPill != null) UpdateToggleVisual(ToggleVolPill, SettingsConfig.Current.EnableVolumeFlyout);
                if (ToggleNativeMediaPill != null) UpdateToggleVisual(ToggleNativeMediaPill, SettingsConfig.Current.HideWindowsMediaFlyout);
                if (ToggleAutoPill != null) UpdateToggleVisual(ToggleAutoPill, SettingsConfig.Current.AutoStartWithWindows);
                if (ToggleLockPill != null) UpdateToggleVisual(ToggleLockPill, SettingsConfig.Current.EnableLockKeysFlyout);
                if (ToggleTitlePill != null) UpdateToggleVisual(ToggleTitlePill, SettingsConfig.Current.AdaptiveTitleColor);

                RefreshDashboard();
            }
            finally
            {
                _isLoaded = true;
            }
        }

        private void SliderMediaDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _isSwitchingTabs) return;

            double val = Math.Round(e.NewValue, 1);
            SettingsConfig.Current.MediaFlyoutDuration = val;
            if (TxtMediaDuration != null) TxtMediaDuration.Text = $"{val:0.0}s";
            SettingsConfig.Save();

            App.MediaWindowInstance?.UpdateDurationFromSettings();
            App.MediaWindowInstance?.ShowAndTriggerTimer();
        }

        private void SwitchView(ScrollViewer activeView, FrameworkElement? activeIndicator)
        {
            _isSwitchingTabs = true;
            try
            {
                if (ViewDashboard != null) ViewDashboard.Visibility = Visibility.Collapsed;
                if (ViewVisualizer != null) ViewVisualizer.Visibility = Visibility.Collapsed;
                if (ViewMixer != null) ViewMixer.Visibility = Visibility.Collapsed;
                if (ViewMedia != null) ViewMedia.Visibility = Visibility.Collapsed;
                if (ViewVolume != null) ViewVolume.Visibility = Visibility.Collapsed;
                if (ViewLock != null) ViewLock.Visibility = Visibility.Collapsed;
                if (ViewSystem != null) ViewSystem.Visibility = Visibility.Collapsed;

                if (IndHome != null) IndHome.Visibility = Visibility.Collapsed;
                if (IndVis != null) IndVis.Visibility = Visibility.Collapsed;
                if (IndMixer != null) IndMixer.Visibility = Visibility.Collapsed;
                if (IndMedia != null) IndMedia.Visibility = Visibility.Collapsed;
                if (IndVol != null) IndVol.Visibility = Visibility.Collapsed;
                if (IndLock != null) IndLock.Visibility = Visibility.Collapsed;
                if (IndSys != null) IndSys.Visibility = Visibility.Collapsed;

                if (activeView != null) activeView.Visibility = Visibility.Visible;
                if (activeIndicator != null) activeIndicator.Visibility = Visibility.Visible;
            }
            finally
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _isSwitchingTabs = false;
                }));
            }
        }

        private void NavHome_Click(object sender, RoutedEventArgs e)
        {
            RefreshDashboard();
            SwitchView(ViewDashboard, IndHome);
        }
        private void NavVisualizer_Click(object sender, RoutedEventArgs e) => SwitchView(ViewVisualizer, IndVis);
        private void NavMixer_Click(object sender, RoutedEventArgs e) => SwitchView(ViewMixer, IndMixer);
        private void NavMedia_Click(object sender, RoutedEventArgs e) => SwitchView(ViewMedia, IndMedia);
        private void NavVolume_Click(object sender, RoutedEventArgs e) => SwitchView(ViewVolume, IndVol);
        private void NavLock_Click(object sender, RoutedEventArgs e) => SwitchView(ViewLock, IndLock);
        private void NavSystem_Click(object sender, RoutedEventArgs e) => SwitchView(ViewSystem, IndSys);

        private void ToggleVisualizer_Click(object sender, RoutedEventArgs e)
        {
            SettingsConfig.Current.EnableTaskbarVisualizer = !SettingsConfig.Current.EnableTaskbarVisualizer;
            if (ToggleVisPill != null) UpdateToggleVisual(ToggleVisPill, SettingsConfig.Current.EnableTaskbarVisualizer);
            SettingsConfig.Save();
            App.VisualizerInstance?.CheckEnabledState();
            RefreshDashboard();
        }

        private void CmbVisStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _isSwitchingTabs || CmbVisStyle == null || CmbVisStyle.SelectedIndex < 0) return;

            SettingsConfig.Current.VisualizerStyle = CmbVisStyle.SelectedIndex switch
            {
                1 => "BottomBars",
                2 => "FluidWave",
                _ => "CenterBars"
            };
            SettingsConfig.Save();

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                App.VisualizerInstance?.RebuildBars();
            }));
        }

        private void CmbVisMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _isSwitchingTabs || CmbVisMonitor == null || CmbVisMonitor.SelectedIndex < 0) return;

            SettingsConfig.Current.VisualizerMonitorIndex = CmbVisMonitor.SelectedIndex;
            SettingsConfig.Save();

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                App.VisualizerInstance?.ApplySettingsLayout();
            }));
        }

        private void SliderVisOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _isSwitchingTabs) return;

            SettingsConfig.Current.VisualizerOffset = e.NewValue;
            if (TxtVisOffset != null) TxtVisOffset.Text = $"{(int)e.NewValue}px";
            SettingsConfig.Save();

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                App.VisualizerInstance?.ApplySettingsLayout();
            }));
        }

        private void SliderBarCount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _isSwitchingTabs) return;

            SettingsConfig.Current.VisualizerBarCount = (int)e.NewValue;
            if (TxtBarCount != null) TxtBarCount.Text = $"{SettingsConfig.Current.VisualizerBarCount} bars";
            SettingsConfig.Save();

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                App.VisualizerInstance?.RebuildBars();
            }));
        }

        private void SliderSensitivity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _isSwitchingTabs) return;

            SettingsConfig.Current.AudioSensitivity = Math.Round(e.NewValue, 1);
            if (TxtSensitivity != null) TxtSensitivity.Text = $"{SettingsConfig.Current.AudioSensitivity:0.0}x";
            SettingsConfig.Save();
        }

        private void SliderSpacing_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded || _isSwitchingTabs) return;

            SettingsConfig.Current.VisualizerTitleSpacing = e.NewValue;
            if (TxtSpacing != null) TxtSpacing.Text = $"{(int)e.NewValue}px";
            SettingsConfig.Save();

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                App.VisualizerInstance?.ApplySettingsLayout();
            }));
        }

        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string themeName)
            {
                SettingsConfig.Current.VisualizerTheme = themeName;
                SettingsConfig.Save();

                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    App.VisualizerInstance?.ApplyCurrentTheme();
                }));
            }
        }

        private void ApplyDynamic_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TxtDynColorA == null || TxtDynColorB == null) return;
                string hexA = TxtDynColorA.Text.Trim();
                string hexB = TxtDynColorB.Text.Trim();

                ColorConverter.ConvertFromString(hexA);
                ColorConverter.ConvertFromString(hexB);

                SettingsConfig.Current.DynamicColorA = hexA;
                SettingsConfig.Current.DynamicColorB = hexB;
                SettingsConfig.Current.VisualizerTheme = "Dynamic";
                SettingsConfig.Save();

                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    App.VisualizerInstance?.ApplyCurrentTheme();
                }));
            }
            catch
            {
                System.Windows.MessageBox.Show("Please enter valid Hex colors for both Color 1 and Color 2.");
            }
        }

        private void ApplyHex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TxtCustomHex == null) return;
                string hex = TxtCustomHex.Text.Trim();
                ColorConverter.ConvertFromString(hex);
                SettingsConfig.Current.CustomHexColor = hex;
                SettingsConfig.Current.VisualizerTheme = "Custom";
                SettingsConfig.Save();

                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    App.VisualizerInstance?.ApplyCurrentTheme();
                }));
            }
            catch
            {
                System.Windows.MessageBox.Show("Please enter a valid Hex color (e.g. #5EEAD4).");
            }
        }

        // Interactive Media Flyout Handlers
        private void CmbMediaMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded && CmbMediaMonitor != null && CmbMediaMonitor.SelectedIndex >= 0)
            {
                SettingsConfig.Current.MediaFlyoutMonitorIndex = CmbMediaMonitor.SelectedIndex;
                SettingsConfig.Save();
                App.MediaWindowInstance?.ApplyPlacementSettings();
                App.MediaWindowInstance?.ShowAndTriggerTimer();
            }
        }

        private void CmbPlacement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded && CmbPlacement != null && CmbPlacement.SelectedIndex >= 0)
            {
                SettingsConfig.Current.MediaFlyoutPlacement = CmbPlacement.SelectedIndex;
                SettingsConfig.Save();
                App.MediaWindowInstance?.ApplyPlacementSettings();
                App.MediaWindowInstance?.ShowAndTriggerTimer();
            }
        }

        private void CmbVolMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded && CmbVolMonitor != null && CmbVolMonitor.SelectedIndex >= 0)
            {
                SettingsConfig.Current.VolumeFlyoutMonitorIndex = CmbVolMonitor.SelectedIndex;
                SettingsConfig.Save();
                App.VolumeFlyoutInstance?.PreviewFlyout();
            }
        }

        private void CmbVolPlacement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded && CmbVolPlacement != null && CmbVolPlacement.SelectedIndex >= 0)
            {
                SettingsConfig.Current.VolumeFlyoutPlacement = CmbVolPlacement.SelectedIndex;
                SettingsConfig.Save();
                App.VolumeFlyoutInstance?.PreviewFlyout();
            }
        }

        private void CmbLockMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded && CmbLockMonitor != null && CmbLockMonitor.SelectedIndex >= 0)
            {
                SettingsConfig.Current.LockKeysMonitorIndex = CmbLockMonitor.SelectedIndex;
                SettingsConfig.Save();
                App.LockKeysInstance?.PreviewFlyout();
            }
        }

        private void CmbLockPlacement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded && CmbLockPlacement != null && CmbLockPlacement.SelectedIndex >= 0)
            {
                SettingsConfig.Current.LockKeysPlacement = CmbLockPlacement.SelectedIndex;
                SettingsConfig.Save();
                App.LockKeysInstance?.PreviewFlyout();
            }
        }

        private void ToggleVolumeFlyout_Click(object sender, RoutedEventArgs e)
        {
            SettingsConfig.Current.EnableVolumeFlyout = !SettingsConfig.Current.EnableVolumeFlyout;
            if (ToggleVolPill != null) UpdateToggleVisual(ToggleVolPill, SettingsConfig.Current.EnableVolumeFlyout);
            SettingsConfig.Save();

            // Turning it off also dismisses a flyout that is currently on screen
            if (!SettingsConfig.Current.EnableVolumeFlyout)
                App.VolumeFlyoutInstance?.HideFlyout();
        }

        private void ToggleLockFlyout_Click(object sender, RoutedEventArgs e)
        {
            SettingsConfig.Current.EnableLockKeysFlyout = !SettingsConfig.Current.EnableLockKeysFlyout;
            if (ToggleLockPill != null) UpdateToggleVisual(ToggleLockPill, SettingsConfig.Current.EnableLockKeysFlyout);
            SettingsConfig.Save();
        }

        private void ToggleTitleColor_Click(object sender, RoutedEventArgs e)
        {
            SettingsConfig.Current.AdaptiveTitleColor = !SettingsConfig.Current.AdaptiveTitleColor;
            if (ToggleTitlePill != null) UpdateToggleVisual(ToggleTitlePill, SettingsConfig.Current.AdaptiveTitleColor);
            SettingsConfig.Save();

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                App.VisualizerInstance?.ApplyCurrentTheme();
            }));
        }

        // ---- Dashboard ----
        private void TestMedia_Click(object sender, RoutedEventArgs e) => App.MediaWindowInstance?.ShowAndTriggerTimer();
        private void TestVolume_Click(object sender, RoutedEventArgs e) => App.VolumeFlyoutInstance?.PreviewFlyout(true);
        private void TestLock_Click(object sender, RoutedEventArgs e) => App.LockKeysInstance?.PreviewFlyout(true);

        private void RefreshDashboard()
        {
            var c = SettingsConfig.Current;

            SetStat(StatVis, c.EnableTaskbarVisualizer);
            SetStat(StatVol, c.EnableVolumeFlyout);
            SetStat(StatLock, c.EnableLockKeysFlyout);
            SetStat(StatAuto, c.AutoStartWithWindows);

            int enabled = (c.EnableTaskbarVisualizer ? 1 : 0)
                        + (c.EnableVolumeFlyout ? 1 : 0)
                        + (c.EnableLockKeysFlyout ? 1 : 0)
                        + (c.AutoStartWithWindows ? 1 : 0);

            if (DashSummary != null)
                DashSummary.Text = $"{enabled} of 4 features enabled";
        }

        private static void SetStat(TextBlock? label, bool on)
        {
            if (label == null) return;
            label.Text = on ? "Enabled" : "Disabled";
            label.Foreground = new SolidColorBrush(on
                ? Color.FromRgb(94, 234, 212)
                : Color.FromRgb(120, 128, 146));
        }

        private void ToggleNativeMediaFlyout_Click(object sender, RoutedEventArgs e)
        {
            SettingsConfig.Current.HideWindowsMediaFlyout = !SettingsConfig.Current.HideWindowsMediaFlyout;

            if (ToggleNativeMediaPill != null)
                UpdateToggleVisual(ToggleNativeMediaPill, SettingsConfig.Current.HideWindowsMediaFlyout);

            SettingsConfig.Save();
        }

        private void ToggleAutoStart_Click(object sender, RoutedEventArgs e)
        {
            SettingsConfig.Current.AutoStartWithWindows = !SettingsConfig.Current.AutoStartWithWindows;
            if (ToggleAutoPill != null) UpdateToggleVisual(ToggleAutoPill, SettingsConfig.Current.AutoStartWithWindows);
            SettingsConfig.ApplyAutoStart(SettingsConfig.Current.AutoStartWithWindows);
            SettingsConfig.Save();
            RefreshDashboard();
        }

        private void UpdateToggleVisual(Border badge, bool isEnabled)
        {
            badge.Background = isEnabled ? new SolidColorBrush(Color.FromArgb(48, 94, 234, 212)) : new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
            badge.BorderBrush = isEnabled ? new SolidColorBrush(Color.FromArgb(180, 94, 234, 212)) : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            if (badge.Child is FrameworkElement thumb)
            {
                thumb.HorizontalAlignment = isEnabled ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    }
}
