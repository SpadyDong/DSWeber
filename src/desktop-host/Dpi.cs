using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshTray
{
    /// <summary>
    /// DPI 感知与缩放。所有自绘界面（悬浮球、气泡）都按当前显示器 DPI 缩放，
    /// 否则在高缩放显示器上会被 Windows 位图拉伸而变模糊。
    /// </summary>
    internal static class Dpi
    {
        public const int BaseDpi = 96;

        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType,
            out uint dpiX, out uint dpiY);

        /// <summary>启用进程级 DPI 感知，必须在创建任何窗口之前调用。</summary>
        public static void Enable()
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2（Win10 1703+）
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch { }

            // PROCESS_PER_MONITOR_DPI_AWARE（Win8.1+）
            try { if (SetProcessDpiAwareness(2) == 0) return; }
            catch { }

            try { SetProcessDPIAware(); }
            catch { }
        }

        /// <summary>该显示器的工作区 DPI（失败时回落到 96）。</summary>
        public static uint DpiForScreen(Screen screen)
        {
            if (screen != null)
            {
                try
                {
                    var pt = new POINT
                    {
                        X = screen.Bounds.Left + screen.Bounds.Width / 2,
                        Y = screen.Bounds.Top + screen.Bounds.Height / 2
                    };

                    IntPtr monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
                    uint dpiX;
                    uint dpiY;
                    if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) == 0 && dpiX > 0)
                        return dpiX;
                }
                catch { }
            }

            return BaseDpi;
        }

        public static float ScaleForScreen(Screen screen)
        {
            return DpiForScreen(screen) / (float)BaseDpi;
        }

        public static float ScaleForWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 1f;

            uint dpi = 0;
            try { dpi = GetDpiForWindow(hwnd); }
            catch { }

            return dpi == 0 ? 1f : dpi / (float)BaseDpi;
        }

        public static int Scaled(int baseValue, float scale)
        {
            int value = (int)Math.Round(baseValue * scale);
            return value < 1 ? 1 : value;
        }

        /// <summary>缩放间距；0 保持为 0（<see cref="Scaled"/> 会把 0 抬到 1）。</summary>
        public static int ScaledSpacing(int baseValue, float scale)
        {
            if (baseValue <= 0) return 0;
            return Scaled(baseValue, scale);
        }
    }
}