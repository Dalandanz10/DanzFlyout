using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DanzFlyout
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        public static MainWindow? MediaWindowInstance { get; private set; }
        public static TaskbarVisualizer? VisualizerInstance { get; private set; }
        public static SettingsWindow? SettingsWindowInstance { get; private set; }
        public static VolumeFlyout? VolumeFlyoutInstance { get; private set; }
        public static LockKeysFlyout? LockKeysInstance { get; private set; }

        public App()
        {
            // Catch UI thread unhandled exceptions
            DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show($"UI Thread Crash:\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\nStack:\n{e.Exception.StackTrace}", "DanzFlyout Error");
                e.Handled = true;
            };

            // Catch background thread unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show($"Background Thread Crash:\n{ex?.GetType().Name}: {ex?.Message}\n\nStack:\n{ex?.StackTrace}", "DanzFlyout Error");
            };

            // Catch async task unhandled exceptions
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                MessageBox.Show($"Task Crash:\n{e.Exception.Message}\n\nStack:\n{e.Exception.StackTrace}", "DanzFlyout Error");
                e.SetObserved();
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "Global\\DanzFlyout_SingleInstance_Mutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            try
            {
                SettingsConfig.Load();
                TrayIconService.Initialize();

                VolumeFlyoutInstance = new VolumeFlyout();
                VolumeFlyoutInstance.Show();

                LockKeysInstance = new LockKeysFlyout();
                LockKeysInstance.Show();

                MediaWindowInstance = new MainWindow();
                MediaWindowInstance.Show();

                VisualizerInstance = new TaskbarVisualizer();
                VisualizerInstance.Show();

                SettingsWindowInstance = new SettingsWindow();

                KeyboardHookHelper.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup Error:\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}", "DanzFlyout Startup Error");
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            KeyboardHookHelper.Stop();
            _singleInstanceMutex?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}