using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DanzFlyout
{
    public static class KeyboardHookHelper
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYUP = 0x0101;
        private const int WM_KEYDOWN = 0x0100;

        private const int VK_CAPITAL = 0x14; // Caps Lock
        private const int VK_NUMLOCK = 0x90; // Num Lock
        private const int VK_SCROLL  = 0x91; // Scroll Lock
        
        private const int VK_VOLUME_MUTE = 0xAD;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_UP   = 0xAF;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        // Keep a static reference to prevent garbage collection
        private static readonly LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookId = IntPtr.Zero;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        public static void Start()
        {
            if (_hookId == IntPtr.Zero)
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                IntPtr modHandle = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, modHandle, 0);
            }
        }

        public static void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

       private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                int vkCode = Marshal.ReadInt32(lParam);

               // Handle Volume Keys on KeyDown
if (msg == WM_KEYDOWN)
{
    if (vkCode == VK_VOLUME_MUTE || vkCode == VK_VOLUME_DOWN || vkCode == VK_VOLUME_UP)
    {
        if (!FullscreenHelper.IsGameOrFullscreenActive())
        {
            App.VolumeFlyoutInstance?.PreviewFlyout();
        }
    }
}

                // Handle Lock Keys on KEYUP so Windows has already toggled the internal LED state
                if (msg == WM_KEYUP)
                {
                    if (!FullscreenHelper.IsGameOrFullscreenActive())
                    {
                        if (vkCode == VK_CAPITAL)
                        {
                            bool isToggled = (GetKeyState(VK_CAPITAL) & 1) != 0;
                            App.LockKeysInstance?.ShowStatus("Caps Lock", isToggled);
                        }
                        else if (vkCode == VK_NUMLOCK)
                        {
                            bool isToggled = (GetKeyState(VK_NUMLOCK) & 1) != 0;
                            App.LockKeysInstance?.ShowStatus("Num Lock", isToggled);
                        }
                        else if (vkCode == VK_SCROLL)
                        {
                            bool isToggled = (GetKeyState(VK_SCROLL) & 1) != 0;
                            App.LockKeysInstance?.ShowStatus("Scroll Lock", isToggled);
                        }
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}