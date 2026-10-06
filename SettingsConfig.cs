using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DanzFlyout
{
    public class SettingsConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DanzFlyout",
            "settings.json"
        );

        public static SettingsConfig Current { get; set; } = new SettingsConfig();

        public bool EnableTaskbarVisualizer { get; set; } = true;
        public string VisualizerStyle { get; set; } = "CenterBars";
        public string VisualizerMode { get; set; } = "FluentWave";
        public int VisualizerMonitorIndex { get; set; } = 0;
        public double VisualizerOffset { get; set; } = 0;
        public int VisualizerBarCount { get; set; } = 14;
        public double AudioSensitivity { get; set; } = 1.0;
        public double VisualizerTitleSpacing { get; set; } = 14;
        public string VisualizerTheme { get; set; } = "Adaptive";
        public string CustomHexColor { get; set; } = "#5EEAD4";

        public string DynamicColorA { get; set; } = "#3B82F6";
        public string DynamicColorB { get; set; } = "#EF4444";

        public int MediaFlyoutMonitorIndex { get; set; } = 0;
        public int MediaFlyoutPlacement { get; set; } = 0;
        public double MediaFlyoutDuration { get; set; } = 3.5; // In seconds (1.0s to 10.0s)

        public int VolumeFlyoutMonitorIndex { get; set; } = 0;
        public int VolumeFlyoutPlacement { get; set; } = 5;

        public int LockKeysMonitorIndex { get; set; } = 0;
        public int LockKeysPlacement { get; set; } = 4;

        public bool EnableVolumeFlyout { get; set; } = true;
        public bool EnableLockKeysFlyout { get; set; } = true;
        public bool AdaptiveTitleColor { get; set; } = true;
        public bool HideWindowsMediaFlyout { get; set; } = true;
        public bool AutoStartWithWindows { get; set; } = false;
        public bool EnableMediaFlyout { get; set; } = false;
        
        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    Current = JsonSerializer.Deserialize<SettingsConfig>(json) ?? new SettingsConfig();
                }
            }
            catch
            {
                Current = new SettingsConfig();
            }

            // Re-sync the startup entry on every launch so it always points at the
            // exe that is actually running (fixes moved/rebuilt/packed exe paths).
            ApplyAutoStart(Current.AutoStartWithWindows);
        }

        public static void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string AppValueName = "DanzFlyout";

        public static void ApplyAutoStart(bool enable)
        {
            try
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                if (runKey == null) return;

                string exePath = Environment.ProcessPath ?? string.Empty;

                // When debugging with "dotnet run" the process is dotnet.exe, which must never be registered.
                bool isDotnetHost = Path.GetFileNameWithoutExtension(exePath)
                    .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

                if (enable && !string.IsNullOrEmpty(exePath) && !isDotnetHost)
                {
                    runKey.SetValue(AppValueName, $"\"{exePath}\"", RegistryValueKind.String);

                    // Windows keeps a separate "approved" flag (Task Manager > Startup apps).
                    // If it was ever switched off there, the Run entry alone is ignored.
                    using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath, true);
                    approved?.SetValue(AppValueName,
                        new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                        RegistryValueKind.Binary);
                }
                else
                {
                    runKey.DeleteValue(AppValueName, false);

                    using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true);
                    approved?.DeleteValue(AppValueName, false);
                }
            }
            catch { }
        }
    }
}
