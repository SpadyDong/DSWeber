using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class BalancePopup : Form
    {
        private const int PopupWidth = 248;
        private const int PaddingX = 16;
        private const int PaddingY = 14;
        private const int Gap = 10;
        private const int CornerRadius = 10;
        private const int AutoCloseMs = 3000;

        private static readonly Color Accent = Color.FromArgb(77, 107, 254);
        private static readonly Color ErrorColor = Color.FromArgb(214, 69, 69);
        private static readonly Color TextPrimary = Color.FromArgb(32, 34, 40);
        private static readonly Color TextMuted = Color.FromArgb(130, 134, 145);

        private readonly Font _fontTitle = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        private readonly Font _fontAmount = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        private readonly Font _fontBody = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        private readonly Font _fontSmall = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular);

        private readonly Timer _closeTimer;
        private Point _anchor;
        private Rectangle _ballBounds;

        private HookProc _hookProc;
        private IntPtr _hookHandle = IntPtr.Zero;

        public BalancePopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(PopupWidth, 80);

            _closeTimer = new Timer();
            _closeTimer.Interval = AutoCloseMs;
            _closeTimer.Tick += delegate { HidePopup(); };

            MouseUp += delegate { HidePopup(); };
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

        public void ShowLoading(Rectangle ballBounds)
        {
            _ballBounds = ballBounds;
            _anchor = new Point(ballBounds.Right, ballBounds.Top + ballBounds.Height / 2);
            Render(BuildLoading());
            _closeTimer.Stop();
            if (!Visible) Show();
            InstallHook();
        }

        public void ShowResult(BalanceResult result)
        {
            if (result == null) return;
            Render(result.Ok ? BuildSuccess(result) : BuildError(result));
            if (!Visible) Show();
            InstallHook();
            _closeTimer.Stop();
            _closeTimer.Start();
        }

        public void Follow(Rectangle ballBounds)
        {
            _ballBounds = ballBounds;
            _anchor = new Point(ballBounds.Right, ballBounds.Top + ballBounds.Height / 2);
            if (!Visible) return;

            var target = Reposition(Size);
            if (target != Location) Location = target;
        }

        public void HidePopup()
        {
            _closeTimer.Stop();
            RemoveHook();
            if (Visible) Hide();
        }

        private Bitmap BuildLoading()
        {
            var lines = new[]
            {
                new TextLine("DeepSeek 余额", _fontTitle, TextMuted, 0),
                new TextLine("正在查询…", _fontBody, TextPrimary, 6)
            };
            return Compose(lines, Accent);
        }

        private Bitmap BuildSuccess(BalanceResult r)
        {
            string symbol = string.Equals(r.Currency, "CNY", StringComparison.OrdinalIgnoreCase) ? "¥"
                          : string.Equals(r.Currency, "USD", StringComparison.OrdinalIgnoreCase) ? "$"
                          : "";

            string amount = symbol + (string.IsNullOrEmpty(r.TotalBalance) ? "-" : r.TotalBalance);
            string detail = "赠送 " + Blank(r.GrantedBalance) + "   充值 " + Blank(r.ToppedUpBalance);
            string footer = (r.IsAvailable ? "账户可用" : "账户不可用")
                          + (string.IsNullOrEmpty(r.KeySource) ? "" : "   密钥来源：" + r.KeySource);

            var lines = new[]
            {
                new TextLine("DeepSeek 余额", _fontTitle, TextMuted, 0),
                new TextLine(amount, _fontAmount, TextPrimary, 2),
                new TextLine(detail, _fontBody, TextMuted, 4),
                new TextLine(footer, _fontSmall, r.IsAvailable ? TextMuted : ErrorColor, 6)
            };
            return Compose(lines, r.IsAvailable ? Accent : ErrorColor);
        }

        private Bitmap BuildError(BalanceResult r)
        {
            var lines = string.IsNullOrEmpty(r.ErrorDetail)
                ? new[]
                {
                    new TextLine("DeepSeek 余额", _fontTitle, TextMuted, 0),
                    new TextLine(r.ErrorText, _fontBody, ErrorColor, 6)
                }
                : new[]
                {
                    new TextLine("DeepSeek 余额", _fontTitle, TextMuted, 0),
                    new TextLine(r.ErrorText, _fontBody, ErrorColor, 6),
                    new TextLine(r.ErrorDetail, _fontSmall, TextMuted, 5)
                };
            return Compose(lines, ErrorColor);
        }

        private static string Blank(string s)
        {
            return string.IsNullOrEmpty(s) ? "-" : s;
        }

        private sealed class TextLine
        {
            public readonly string Text;
            public readonly Font Font;
            public readonly Color Color;
            public readonly int SpaceBefore;

            public TextLine(string text, Font font, Color color, int spaceBefore)
            {
                Text = text == null ? "" : text;
                Font = font;
                Color = color;
                SpaceBefore = spaceBefore;
            }
        }

        private Bitmap Compose(TextLine[] lines, Color stripe)
        {
            int contentWidth = PopupWidth - PaddingX * 2;

            var heights = new int[lines.Length];
            int total = PaddingY * 2;

            using (var probe = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(probe))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                for (int i = 0; i < lines.Length; i++)
                {
                    var size = g.MeasureString(lines[i].Text, lines[i].Font, contentWidth);
                    heights[i] = (int)Math.Ceiling(size.Height);
                    total += heights[i] + lines[i].SpaceBefore;
                }
            }

            var bmp = new Bitmap(PopupWidth, total, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                var body = new Rectangle(0, 0, PopupWidth - 1, total - 1);

                using (var shadowPath = RoundedRect(new Rectangle(1, 2, body.Width, body.Height), CornerRadius))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
                {
                    g.FillPath(shadowBrush, shadowPath);
                }

                using (var path = RoundedRect(body, CornerRadius))
                {
                    using (var back = new SolidBrush(Color.FromArgb(246, 255, 255, 255)))
                    {
                        g.FillPath(back, path);
                    }
                    using (var border = new Pen(Color.FromArgb(48, stripe), 1.2f))
                    {
                        g.DrawPath(border, path);
                    }

                    var saved = g.Clip;
                    g.SetClip(path);
                    using (var accentBrush = new SolidBrush(stripe))
                    {
                        g.FillRectangle(accentBrush, 0, 0, 3, total);
                    }
                    g.Clip = saved;
                }

                int y = PaddingY;
                var layout = StringFormat.GenericTypographic;
                for (int i = 0; i < lines.Length; i++)
                {
                    y += lines[i].SpaceBefore;
                    using (var brush = new SolidBrush(lines[i].Color))
                    {
                        g.DrawString(lines[i].Text, lines[i].Font, brush,
                            new RectangleF(PaddingX, y, contentWidth, heights[i]), layout);
                    }
                    y += heights[i];
                }
            }

            return bmp;
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Render(Bitmap surface)
        {
            using (surface)
            {
                Size = surface.Size;
                Location = Reposition(surface.Size);
                if (!IsHandleCreated)
                {
                    var force = Handle;
                    GC.KeepAlive(force);
                }
                ApplyLayeredBitmap(surface);
            }
        }

        private Point Reposition(Size size)
        {
            var screen = Screen.FromPoint(_anchor);
            var area = screen.WorkingArea;

            int x = _anchor.X + Gap;
            if (x + size.Width > area.Right) x = _anchor.X - Gap - size.Width - FloatingBall.BallSize;
            if (x < area.Left) x = area.Left + Gap;

            int y = _anchor.Y - size.Height / 2;
            if (y + size.Height > area.Bottom) y = area.Bottom - size.Height - Gap;
            if (y < area.Top) y = area.Top + Gap;

            return new Point(x, y);
        }

        #region Outside click hook

        private const int WH_MOUSE_LL = 14;
        private const int HC_ACTION = 0;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_NCLBUTTONDOWN = 0x00A1;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT Pt;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr DwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private void InstallHook()
        {
            if (_hookHandle != IntPtr.Zero) return;

            _hookProc = OnMouseHook;
            IntPtr module = GetModuleHandle(null);
            _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, module, 0);
        }

        private void RemoveHook()
        {
            if (_hookHandle == IntPtr.Zero) return;

            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _hookProc = null;
        }

        private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == HC_ACTION)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN
                    || msg == WM_MBUTTONDOWN || msg == WM_NCLBUTTONDOWN)
                {
                    var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    var pt = new Point(data.Pt.X, data.Pt.Y);

                    if (!Bounds.Contains(pt) && !_ballBounds.Contains(pt))
                    {
                        BeginInvoke((MethodInvoker)delegate { HidePopup(); });
                    }
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RemoveHook();
                _closeTimer.Dispose();
                _fontTitle.Dispose();
                _fontAmount.Dispose();
                _fontBody.Dispose();
                _fontSmall.Dispose();
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

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
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memDc, hBitmap);

                var size = new SIZE { Cx = bitmap.Width, Cy = bitmap.Height };
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
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                    DeleteObject(hBitmap);
                }
                DeleteDC(memDc);
            }
        }

        #endregion
    }
}
