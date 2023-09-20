using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DeathCounter
{
    public static class Utilities
    {
        #region DLL Libraries
        // DLL libraries used to manage hotkeys
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        //https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-hotkey
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        public const int GWL_EX_STYLE = -20;
        public const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;

        [Flags]
        private enum ProcessAccessFlags : uint
        {
            QueryLimitedInformation = 0x00001000
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName( [In] IntPtr hProcess, [In] int dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(ProcessAccessFlags processAccess, bool bInheritHandle, int processId);
        #endregion

        public static String GetProcessFilename(Process p)
        {
            int capacity = 2000;
            StringBuilder builder = new StringBuilder(capacity);
            IntPtr ptr = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, p.Id);
            if (!QueryFullProcessImageName(ptr, 0, builder, ref capacity))
            {
                return String.Empty;
            }

            return builder.ToString();
        }

        public static Bitmap InvertBitmap(Bitmap bmp)
        {
            LockBitmap lockBitmap = new LockBitmap(bmp);
            lockBitmap.LockBits();
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    // get pixel value
                    Color p = lockBitmap.GetPixel(x, y);

                    // extract ARGB value from p
                    int a = p.A;
                    int r = p.R;
                    int g = p.G;
                    int b = p.B;

                    // find negative value
                    r = 255 - r; // Normally this should be 255 - r, but 4 helps for remnant 2 specifically
                    g = 255 - g;
                    b = 255 - b;

                    // set new ARGB value in pixel
                    lockBitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                }
            }
            lockBitmap.UnlockBits();
            return bmp;
        }
    }
}
