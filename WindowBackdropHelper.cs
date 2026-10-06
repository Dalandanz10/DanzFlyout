using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DanzFlyout
{
    public static class WindowBackdropHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // Windows 11 DWM backdrop attributes
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // 1 = None, 2 = Mica, 3 = Acrylic (Frosted Glass), 4 = Tabbed
        private const int DWMSBT_TRANSIENTWINDOW = 3;

        public static void ApplyAcrylic(IntPtr hwnd, bool darkMode = true)
        {
            int darkValue = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));

            int backdropType = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }
    }
}