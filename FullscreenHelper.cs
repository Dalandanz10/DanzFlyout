using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DanzFlyout
{
    public static class FullscreenHelper
    {
        private const int GWL_STYLE = -16;
        private const long WS_CAPTION = 0x00C00000L;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RealGetWindowClass(IntPtr hwnd, StringBuilder pszType, uint cchType);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private static long GetWindowStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, GWL_STYLE).ToInt64();
            return GetWindowLong32(hWnd, GWL_STYLE);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public static bool IsGameOrFullscreenActive()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == GetDesktopWindow() || fg == GetShellWindow())
                    return false;

                // Ignore standard Windows desktop shell elements
                var sb = new StringBuilder(256);
                RealGetWindowClass(fg, sb, 256);
                string className = sb.ToString();

                if (className.Contains("Progman") || 
                    className.Contains("WorkerW") || 
                    className.Contains("Shell_TrayWnd") ||
                    className.Contains("Shell_SecondaryTrayWnd"))
                {
                    return false;
                }

                var screen = Screen.FromHandle(fg);

                if (GetWindowRect(fg, out RECT rect))
                {
                    bool coversMonitor = rect.Left <= screen.Bounds.Left &&
                                         rect.Top <= screen.Bounds.Top &&
                                         rect.Right >= screen.Bounds.Right &&
                                         rect.Bottom >= screen.Bounds.Bottom;

                    if (coversMonitor)
                    {
                        long style = GetWindowStyle(fg);
                        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;

                        return !hasCaption || (rect.Bottom - rect.Top >= screen.Bounds.Height);
                    }
                }
            }
            catch { }

            return false;
        }
    }
}