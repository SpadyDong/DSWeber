using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class FloatingBall : Form
    {
        public const int BallSize = 44;
        private const int DragThreshold = 4;

        private static readonly Color ColorRunning = Color.FromArgb(77, 107, 254);
        private static readonly Color ColorStopped = Color.FromArgb(150, 150, 150);
        private static readonly Color ColorCrashed = Color.FromArgb(214, 69, 69);

        private readonly DshSupervisor _supervisor;
        private readonly Action<Point> _onMoved;
        private readonly Action _onMenuShowing;
        private readonly ContextMenuStrip _menu;
        private readonly AppConfig _appConfig;
        private readonly BalancePopup _balancePopup;

        private readonly MenuAnchorWindow _menuAnchor;
        private readonly Timer _flashTimer;
        private bool _flashGray;

        private Point _dragOrigin;
        private Point _formOrigin;
        private bool _dragging;
        private bool _moved;

        public FloatingBall(DshSupervisor supervisor, AppConfig appConfig, Point location, ContextMenuStrip menu, Action<Point> onMoved, Action onMenuShowing)
        {
            _supervisor = supervisor;
            _appConfig = appConfig;
            _balancePopup = new BalancePopup();
            _menu = menu;
            _menuAnchor = new MenuAnchorWindow();
            _onMoved = onMoved;
            _onMenuShowing = onMenuShowing;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(BallSize, BallSize);
            Location = location;

            MouseDown += OnBallMouseDown;
            MouseMove += OnBallMouseMove;
            MouseUp += OnBallMouseUp;

            _flashTimer = new Timer();
            _flashTimer.Interval = 500;
            _flashTimer.Tick += OnFlashTick;
        }

        private void OnFlashTick(object sender, EventArgs e)
        {
            _flashGray = !_flashGray;
            Redraw();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Redraw();
        }

        public void Redraw()
        {
            if (!IsHandleCreated) return;

            Color color;
            switch (_supervisor.State)
            {
                case ServiceState.Running:
                    color = ColorRunning;
                    break;
                case ServiceState.Starting:
                    color = _flashGray ? ColorStopped : ColorRunning;
                    break;
                case ServiceState.Crashed:
                case ServiceState.PortConflict:
                    color = ColorCrashed;
                    break;
                default:
                    color = ColorStopped;
                    break;
            }

            if (_supervisor.State == ServiceState.Starting)
            {
                if (!_flashTimer.Enabled)
                {
                    _flashTimer.Start();
                }
            }
            else if (_flashTimer.Enabled)
            {
                _flashTimer.Stop();
            }

            using (var surface = ComposeBall(color))
            {
                ApplyLayeredBitmap(surface);
            }

            string label = _supervisor.State == ServiceState.Running ? "运行中"
                         : _supervisor.State == ServiceState.Starting ? "启动中"
                         : _supervisor.State == ServiceState.Crashed ? "已异常退出"
                         : _supervisor.State == ServiceState.PortConflict ? "端口冲突"
                         : "已停止";
            AccessibleName = "dsh web - " + label;
        }

        private static Bitmap ComposeBall(Color glyphColor)
        {
            var bmp = new Bitmap(BallSize, BallSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                var plate = new Rectangle(0, 0, BallSize - 1, BallSize - 1);
                using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                {
                    g.FillEllipse(shadow, 1, 2, BallSize - 2, BallSize - 2);
                }
                using (var back = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    g.FillEllipse(back, plate);
                }
                using (var ring = new Pen(Color.FromArgb(60, glyphColor), 1.6f))
                {
                    g.DrawEllipse(ring, plate);
                }

                using (var glyph = IconFactory.CreateGlyphBitmap(BallSize, glyphColor))
                {
                    int inner = (int)(BallSize * 0.66f);
                    int pad = (BallSize - inner) / 2;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(glyph, new Rectangle(pad, pad, inner, inner));
                }
            }
            return bmp;
        }

        private void OnBallMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _balancePopup.HidePopup();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _dragOrigin = Cursor.Position;
            _formOrigin = Location;
        }

        private void OnBallMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            int dx = Cursor.Position.X - _dragOrigin.X;
            int dy = Cursor.Position.Y - _dragOrigin.Y;
            if (!_moved && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
            {
                _moved = true;
            }
            if (_moved)
            {
                Location = ClampToWorkArea(new Point(_formOrigin.X + dx, _formOrigin.Y + dy));
                _balancePopup.Follow(Bounds);
            }
        }

        private void OnBallMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _balancePopup.HidePopup();
                ShowContextMenu();
                return;
            }

            if (e.Button != MouseButtons.Left || !_dragging) return;
            _dragging = false;

            if (_moved)
            {
                if (_onMoved != null) _onMoved(Location);
            }
            else
            {
                ShowBalance();
            }
        }

        internal static Point ClampToWorkArea(Point p)
        {
            var center = new Point(p.X + BallSize / 2, p.Y + BallSize / 2);
            Screen target = null;
            int bestDist = int.MaxValue;

            foreach (var screen in Screen.AllScreens)
            {
                var area = screen.WorkingArea;
                if (area.Contains(center))
                {
                    target = screen;
                    break;
                }

                int cx = Math.Max(area.Left, Math.Min(center.X, area.Right));
                int cy = Math.Max(area.Top, Math.Min(center.Y, area.Bottom));
                int dx = center.X - cx;
                int dy = center.Y - cy;
                int dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    target = screen;
                }
            }

            if (target == null) target = Screen.PrimaryScreen;
            var wa = target.WorkingArea;

            int x = Math.Max(wa.Left, Math.Min(p.X, wa.Right - BallSize));
            int y = Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - BallSize));
            return new Point(x, y);
        }

        private void ShowContextMenu()
        {
            if (_menu == null) return;

            if (_onMenuShowing != null) _onMenuShowing();

            ForceForeground(_menuAnchor.Handle);
            _menu.Show(Cursor.Position);
            PostMessage(_menuAnchor.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }

        private static void ForceForeground(IntPtr hWnd)
        {
            IntPtr current = GetForegroundWindow();
            if (current == hWnd) return;

            uint pid;
            uint foreignThread = GetWindowThreadProcessId(current, out pid);
            uint ownThread = GetCurrentThreadId();

            bool attached = false;
            if (foreignThread != 0 && foreignThread != ownThread)
            {
                attached = AttachThreadInput(ownThread, foreignThread, true);
            }

            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);

            if (attached)
            {
                AttachThreadInput(ownThread, foreignThread, false);
            }
        }

        private sealed class MenuAnchorWindow : NativeWindow, IDisposable
        {
            public MenuAnchorWindow()
            {
                CreateHandle(new CreateParams());
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }

        private void ShowBalance()
        {
            var bounds = Bounds;
            int session = _balancePopup.ShowLoading(bounds);
            BalanceService.QueryAsync(_appConfig, delegate(BalanceResult r)
            {
                _balancePopup.ShowResult(session, r);
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_flashTimer != null)
                {
                    _flashTimer.Stop();
                    _flashTimer.Dispose();
                }
                if (_menuAnchor != null) _menuAnchor.Dispose();
                if (_balancePopup != null)
                {
                    _balancePopup.HidePopup();
                    _balancePopup.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Layered window

        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const uint BI_RGB = 0;
        private const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int Cx;
            public int Cy;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int WM_NULL = 0x0000;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi,
            uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private void ApplyLayeredBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr oldBitmap = IntPtr.Zero;
            IntPtr hDib = IntPtr.Zero;
            IntPtr dibBits = IntPtr.Zero;

            try
            {
                int width = bitmap.Width;
                int height = bitmap.Height;

                var rect = new Rectangle(0, 0, width, height);
                BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] src = new byte[data.Stride * height];
                    Marshal.Copy(data.Scan0, src, 0, src.Length);

                    byte[] premul = new byte[width * 4 * height];
                    int srcStride = data.Stride;
                    int dstStride = width * 4;
                    for (int y = 0; y < height; y++)
                    {
                        Array.Copy(src, y * srcStride, premul, y * dstStride, dstStride);
                    }

                    for (int i = 0; i < premul.Length; i += 4)
                    {
                        byte a = premul[i + 3];
                        premul[i] = (byte)(premul[i] * a / 255);
                        premul[i + 1] = (byte)(premul[i + 1] * a / 255);
                        premul[i + 2] = (byte)(premul[i + 2] * a / 255);
                    }

                    var bmi = new BITMAPINFO();
                    bmi.bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = (int)BI_RGB,
                        biSizeImage = premul.Length,
                        biXPelsPerMeter = 0,
                        biYPelsPerMeter = 0,
                        biClrUsed = 0,
                        biClrImportant = 0
                    };

                    hDib = CreateDIBSection(screenDc, ref bmi, DIB_RGB_COLORS, out dibBits, IntPtr.Zero, 0);
                    if (hDib == IntPtr.Zero || dibBits == IntPtr.Zero)
                    {
                        return;
                    }

                    Marshal.Copy(premul, 0, dibBits, premul.Length);
                    oldBitmap = SelectObject(memDc, hDib);

                    var size = new SIZE { Cx = width, Cy = height };
                    var srcPoint = new POINT { X = 0, Y = 0 };
                    var dstPoint = new POINT { X = Left, Y = Top };
                    var blend = new BLENDFUNCTION
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AC_SRC_ALPHA
                    };

                    UpdateLayeredWindow(Handle, screenDc, ref dstPoint, ref size,
                        memDc, ref srcPoint, 0, ref blend, ULW_ALPHA);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                }
                if (hDib != IntPtr.Zero)
                {
                    DeleteObject(hDib);
                }
                DeleteDC(memDc);
            }
        }

        #endregion
    }
}
