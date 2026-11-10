using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class FloatingBall : Form
    {
        public const int BaseBallSize = 44;
        private const int BaseDragThreshold = 4;

        // 右上角 Codex 剩余量：纯数字文本，无背景（按 Apple HIG 中性色/细描边）
        private const int BaseCodexInsetRight = 5;
        private const int BaseCodexTop = 5;
        private const float BaseCodexFontSize = 7f;
        private const string SevenDayLabel = "7 天";

        private static readonly Color BallColor = Color.FromArgb(77, 107, 254);
        private static readonly Color RingColor = Color.FromArgb(229, 229, 234);
        private static readonly Color CodexTextColor = Color.FromArgb(29, 29, 31);

        private readonly Action<Point> _onMoved;
        private readonly Action _onMenuShowing;
        private readonly ContextMenuStrip _menu;
        private readonly AppConfig _appConfig;
        private readonly BalancePopup _balancePopup;
        private int _balanceSession;

        private readonly MenuAnchorWindow _menuAnchor;
        private readonly Timer _visibilityTimer;

        private Point _dragOrigin;
        private Point _formOrigin;
        private bool _dragging;
        private bool _moved;
        private float _scale = 1f;

        private int? _codexLeftPercent;
        private Font _codexFont;
        private float _codexFontScale = -1f;

        /// <summary>按当前显示器 DPI 缩放后的球体边长。</summary>
        public int BallSize
        {
            get { return Dpi.Scaled(BaseBallSize, _scale); }
        }

        private int DragThreshold
        {
            get { return Dpi.Scaled(BaseDragThreshold, _scale); }
        }

        public FloatingBall(AppConfig appConfig, Point location, ContextMenuStrip menu, Action<Point> onMoved, Action onMenuShowing)
        {
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
            _scale = Dpi.ScaleForScreen(Screen.FromPoint(new Point(
                location.X + BaseBallSize / 2, location.Y + BaseBallSize / 2)));
            Size = new Size(BallSize, BallSize);
            Location = location;

            _codexLeftPercent = PickBadgePercent(CodexQuotaService.GetCached());

            MouseDown += OnBallMouseDown;
            MouseMove += OnBallMouseMove;
            MouseUp += OnBallMouseUp;

            _visibilityTimer = new Timer();
            _visibilityTimer.Interval = 5000;
            _visibilityTimer.Tick += OnVisibilityTick;
        }

        private void OnVisibilityTick(object sender, EventArgs e)
        {
            KeepVisible();
        }

        private void KeepVisible()
        {
            if (!Visible || !IsHandleCreated) return;
            if (!_dragging)
            {
                Point onScreen = ClampToWorkArea(Location, BallSize);
                if (onScreen != Location) Location = onScreen;
            }
            // Window ordering or layered pixels can be lost after desktop changes.
            // Reassert the position without activating the ball, then redraw it.
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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

        protected override void WndProc(ref Message m)
        {
            // 拖到 DPI 不同的显示器时，按新 DPI 重排球体尺寸并重绘，避免被系统拉伸
            if (m.Msg == WM_DPICHANGED)
            {
                float scale = ((m.WParam.ToInt64() >> 16) & 0xFFFF) / (float)Dpi.BaseDpi;
                if (scale <= 0f) scale = 1f;

                if (Math.Abs(scale - _scale) > 0.001f)
                {
                    int centerX = Location.X + BallSize / 2;
                    int centerY = Location.Y + BallSize / 2;

                    _scale = scale;
                    Size = new Size(BallSize, BallSize);
                    Location = ClampToWorkArea(
                        new Point(centerX - BallSize / 2, centerY - BallSize / 2), BallSize);

                    Redraw();
                    _balancePopup.Follow(Bounds);
                }
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Redraw();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _visibilityTimer.Start();
            KeepVisible();
        }

        public void Redraw()
        {
            if (!IsHandleCreated) return;

            using (var surface = ComposeBall(BallColor))
            {
                ApplyLayeredBitmap(surface);
            }

            AccessibleName = "余额悬浮球";
        }

        private Bitmap ComposeBall(Color glyphColor)
        {
            int size = BallSize;
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // 分层窗口带 alpha，ClearType 的彩色次像素会在透明边上出现彩边
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                var plate = new Rectangle(0, 0, size - 1, size - 1);
                using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                {
                    g.FillEllipse(shadow, 1, 2, size - 2, size - 2);
                }
                using (var back = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    g.FillEllipse(back, plate);
                }
                using (var ring = new Pen(RingColor, Math.Max(1f, 1f * _scale)))
                {
                    g.DrawEllipse(ring, plate);
                }

                using (var glyph = IconFactory.CreateGlyphBitmap(size, glyphColor))
                {
                    int inner = (int)(size * 0.54f);
                    int pad = (size - inner) / 2;
                    int glyphTop = pad + Dpi.Scaled(4, _scale);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(glyph, new Rectangle(pad, glyphTop, inner, inner));
                }

                DrawCodexPercent(g);
            }
            return bmp;
        }

        /// <summary>球体右上角画 Codex 剩余百分比，纯文本无背景。</summary>
        private void DrawCodexPercent(Graphics g)
        {
            if (_codexLeftPercent == null) return;

            string text = _codexLeftPercent.Value + "%";
            var font = EnsureCodexFont();
            var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap;

            int textWidth = (int)Math.Ceiling(
                g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width);

            int right = BallSize - Dpi.Scaled(BaseCodexInsetRight, _scale);
            int top = Dpi.Scaled(BaseCodexTop, _scale);

            using (var brush = new SolidBrush(CodexTextColor))
            {
                g.DrawString(text, font, brush,
                    new RectangleF(right - textWidth, top, textWidth, BallSize - top), format);
            }
        }

        private Font EnsureCodexFont()
        {
            if (_codexFont != null && Math.Abs(_codexFontScale - _scale) < 0.001f)
                return _codexFont;

            if (_codexFont != null) _codexFont.Dispose();

            _codexFontScale = _scale;
            _codexFont = new Font("Segoe UI", BaseCodexFontSize * _scale, FontStyle.Bold, GraphicsUnit.Point);
            return _codexFont;
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
                Location = ClampToWorkArea(new Point(_formOrigin.X + dx, _formOrigin.Y + dy), BallSize);
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

        internal static Point ClampToWorkArea(Point p, int ballSize)
        {
            var center = new Point(p.X + ballSize / 2, p.Y + ballSize / 2);
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

            int x = Math.Max(wa.Left, Math.Min(p.X, wa.Right - ballSize));
            int y = Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - ballSize));
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
            bool codexEnabled = _appConfig != null && _appConfig.ShowCodexQuota;

            // 平时由托盘后台每 60 秒刷新缓存，这里优先直接用缓存，避免等待
            BalanceResult cachedBalance = BalanceService.GetCached();
            CodexQuota cachedCodex = codexEnabled ? CodexQuotaService.GetCached() : null;
            ArkPlan cachedArk = ArkPlanService.GetCached();

            int session = _balancePopup.ShowLoading(Bounds, codexEnabled, cachedBalance, cachedCodex, cachedArk);
            _balanceSession = session;

            if (cachedBalance == null)
            {
                BalanceService.QueryAsync(_appConfig, delegate(BalanceResult r)
                {
                    if (!_balancePopup.IsDisposed) _balancePopup.SetBalance(session, r);
                });
            }

            if (codexEnabled && cachedCodex == null)
            {
                CodexQuotaService.QueryAsync(_appConfig, delegate(CodexQuota q)
                {
                    if (!_balancePopup.IsDisposed) _balancePopup.SetCodex(session, q);
                });
            }

            if (cachedArk == null)
            {
                ArkPlanService.QueryAsync(_appConfig, delegate(ArkPlan p)
                {
                    if (!_balancePopup.IsDisposed) _balancePopup.SetArk(session, p);
                });
            }
        }

        public void UpdateBalance(BalanceResult result)
        {
            if (!_balancePopup.IsDisposed && _balancePopup.Visible)
                _balancePopup.SetBalance(_balanceSession, result);
        }

        public void UpdateCodex(CodexQuota result)
        {
            int? percent = PickBadgePercent(result);
            if (percent != _codexLeftPercent)
            {
                _codexLeftPercent = percent;
                Redraw();
            }

            if (!_balancePopup.IsDisposed && _balancePopup.Visible && _appConfig.ShowCodexQuota)
                _balancePopup.SetCodex(_balanceSession, result);
        }

        /// <summary>取 7 天窗口的剩余百分比；无有效数据时返回 null（不显示数字）。</summary>
        private int? PickBadgePercent(CodexQuota quota)
        {
            if (_appConfig == null || !_appConfig.ShowCodexQuota) return null;
            if (quota == null || !quota.Ok || quota.Windows == null) return null;

            foreach (var window in quota.Windows)
            {
                if (string.Equals(window.Label, SevenDayLabel, StringComparison.OrdinalIgnoreCase))
                    return window.LeftPercent;
            }
            return null;
        }

        public void UpdateArk(ArkPlan result)
        {
            if (!_balancePopup.IsDisposed && _balancePopup.Visible)
                _balancePopup.SetArk(_balanceSession, result);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_visibilityTimer != null)
                {
                    _visibilityTimer.Stop();
                    _visibilityTimer.Dispose();
                }
                if (_menuAnchor != null) _menuAnchor.Dispose();
                if (_codexFont != null)
                {
                    _codexFont.Dispose();
                    _codexFont = null;
                }
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
        private const int WM_DPICHANGED = 0x02E0;

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
