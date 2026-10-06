using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanzFlyout
{
    public static class TrayIconService
    {
        private static NotifyIcon? _notifyIcon;
        private static SettingsWindow? _settingsWindow;
        private static readonly object _lock = new();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const uint WM_MOUSEMOVE = 0x0200;

        public static void CleanGhostIcons()
        {
            try
            {
                // Sweep main taskbar notification area
                IntPtr hTray = FindWindow("Shell_TrayWnd", null);
                IntPtr hTrayNotify = FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
                IntPtr hSysPager = FindWindowEx(hTrayNotify, IntPtr.Zero, "SysPager", null);
                IntPtr hToolbar = FindWindowEx(hSysPager, IntPtr.Zero, "ToolbarWindow32", null);
                RefreshToolbar(hToolbar);

                // Sweep chevron overflow tray window
                IntPtr hOverflow = FindWindow("NotifyIconOverflowWindow", null);
                IntPtr hOverflowToolbar = FindWindowEx(hOverflow, IntPtr.Zero, "ToolbarWindow32", null);
                RefreshToolbar(hOverflowToolbar);
            }
            catch { }
        }

        private static void RefreshToolbar(IntPtr hToolbar)
        {
            if (hToolbar == IntPtr.Zero) return;
            if (GetClientRect(hToolbar, out RECT rect))
            {
                for (int x = 0; x < rect.Right; x += 4)
                {
                    for (int y = 0; y < rect.Bottom; y += 4)
                    {
                        IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
                        SendMessage(hToolbar, WM_MOUSEMOVE, IntPtr.Zero, lParam);
                    }
                }
            }
        }

        public static void Initialize()
        {
            CleanGhostIcons();

            lock (_lock)
            {
                if (_notifyIcon != null) return;

                _notifyIcon = new NotifyIcon
                {
                    Text = "DanzFlyout",
                    Icon = LoadAppIcon(),
                    Visible = true
                };

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Settings", null, (s, e) => OpenSettings());
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("Exit", null, (s, e) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        System.Windows.Application.Current.Shutdown();
                    });
                });

                _notifyIcon.ContextMenuStrip = contextMenu;

                _notifyIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        OpenSettings();
                    }
                };
                _notifyIcon.DoubleClick += (s, e) => OpenSettings();
            }
        }

        public static Icon LoadAppIcon()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/Assets/DanzFlyout.ico");
                var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    using var stream = streamInfo.Stream;
                    return new Icon(stream);
                }
            }
            catch { }

            // Fallback: check direct file path
            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DanzFlyout.ico");
                if (File.Exists(localPath))
                {
                    return new Icon(localPath);
                }
            }
            catch { }

            return SystemIcons.Application;
        }

        public static ImageSource GetWindowIconSource()
        {
            using var icon = LoadAppIcon();
            IntPtr hIcon = icon.Handle;
            try
            {
                return Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        public static void OpenSettings()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_settingsWindow == null || !_settingsWindow.IsLoaded)
                {
                    _settingsWindow = new SettingsWindow();
                    _settingsWindow.Closed += (s, e) => _settingsWindow = null;
                    _settingsWindow.Show();
                }
                else
                {
                    if (_settingsWindow.Visibility != Visibility.Visible)
                        _settingsWindow.Show();

                    if (_settingsWindow.WindowState == WindowState.Minimized)
                        _settingsWindow.WindowState = WindowState.Normal;
                }

                _settingsWindow.Activate();
                _settingsWindow.Topmost = true;
                _settingsWindow.Topmost = false;
                _settingsWindow.Focus();
            });
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                if (_settingsWindow != null)
                {
                    try
                    {
                        _settingsWindow.Close();
                    }
                    catch { }
                    _settingsWindow = null;
                }
            }
        }
    }
}