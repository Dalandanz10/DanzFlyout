using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace DanzFlyout
{
    public class AudioSessionItem
    {
        public string ProcessName { get; set; } = "App";
        public string DisplayName { get; set; } = "Application";
        public float Volume { get; set; } = 1.0f;
        public bool IsMuted { get; set; } = false;
        public SimpleAudioVolume? VolumeControl { get; set; }
    }

    public static class AudioMixerService
    {
        public static ObservableCollection<AudioSessionItem> GetActiveSessions()
        {
            var list = new ObservableCollection<AudioSessionItem>();

            try
            {
                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = device.AudioSessionManager;

                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    var ctl = session.SimpleAudioVolume;
                    var pid = session.GetProcessID;

                    if (pid == 0) continue; // Skip system idle

                    string name = "System";
                    try
                    {
                        var proc = Process.GetProcessById((int)pid);
                        name = proc.ProcessName;
                    }
                    catch { }

                    // Filter relevant desktop audio apps
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    list.Add(new AudioSessionItem
                    {
                        ProcessName = name,
                        DisplayName = char.ToUpper(name[0]) + name.Substring(1),
                        Volume = ctl.Volume * 100f,
                        IsMuted = ctl.Mute,
                        VolumeControl = ctl
                    });
                }
            }
            catch { }

            return list;
        }
    }
}