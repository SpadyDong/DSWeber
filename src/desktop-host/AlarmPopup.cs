using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class AlarmPopup : Form
    {
        private const int BasePopupWidth = 260;
        private const int BasePaddingX = 16;
        private const int BasePaddingY = 14;
        private const int BaseCornerRadius = 10;
        private const int BaseScreenMargin = 16;

        private static readonly Color Accent = Color.FromArgb(77, 107, 254);
        private static readonly Color TextPrimary = Color.FromArgb(32, 34, 40);
        private static readonly Color TextMuted = Color.FromArgb(130, 134, 145);

        private float _scale = -1f;
        private int _popupWidth;
        private int _paddingX;
        private int _paddingY;
        private int _cornerRadius;
        private int _screenMargin;
        private Font _fontTitle;
        private Font _fontTime;
        private Font _fontBody;

        public AlarmPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            UpdateScale();

            MouseUp += delegate { ClosePopup(); };
        }

        /// <summary>闹钟气泡固定显示在主屏，按主屏 DPI 重排尺寸与字体。</summary>
        private void UpdateScale()
        {
            float scale = Dpi.ScaleForScreen(Screen.PrimaryScreen);
            if (Math.Abs(scale - _scale) < 0.001f) return;

            _scale = scale;
            _popupWidth = Dpi.Scaled(BasePopupWidth, scale);
            _paddingX = Dpi.Scaled(BasePaddingX, scale);
            _paddingY = Dpi.Scaled(BasePaddingY, scale);
            _cornerRadius = Dpi.Scaled(BaseCornerRadius, scale);
            _screenMargin = Dpi.Scaled(BaseScreenMargin, scale);

            if (_fontTitle != null) _fontTitle.Dispose();
            if (_fontTime != null) _fontTime.Dispose();
            if (_fontBody != null) _fontBody.Dispose();

            _fontTitle = new Font("Microsoft YaHei UI", 9f * scale, FontStyle.Regular);
            _fontTime = new Font("Microsoft YaHei UI", 15f * scale, FontStyle.Bold);
            _fontBody = new Font("Microsoft YaHei UI", 10.5f * scale, FontStyle.Regular);
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

        public void ShowMessage(string time, string message)
        {
            UpdateScale();
            Render(Build(time, message));
            if (!Visible) Show();
        }

        public void ClosePopup()
        {
            if (Visible) Hide();
        }

        private Bitmap Build(string time, string message)
        {
            time = time == null ? "" : time.Trim();
            message = message == null ? "" : message;

            var lines = new[]
            {
                new TextLine("闹钟提醒", _fontTitle, TextMuted, 0),
                new TextLine(time, _fontTime, TextPrimary, 6),
                new TextLine(message, _fontBody, TextPrimary, 6)
            };
            return Compose(lines, Accent);
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
            int contentWidth = _popupWidth - _paddingX * 2;
            int stripeWidth = Dpi.Scaled(3, _scale);

            var heights = new int[lines.Length];
            int total = _paddingY * 2;

            using (var probe = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(probe))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                for (int i = 0; i < lines.Length; i++)
                {
                    var size = g.MeasureString(lines[i].Text, lines[i].Font, contentWidth);
                    heights[i] = (int)Math.Ceiling(size.Height);
                    total += heights[i] + Dpi.ScaledSpacing(lines[i].SpaceBefore, _scale);
                }
            }

            var bmp = new Bitmap(_popupWidth, total, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);

                var body = new Rectangle(0, 0, _popupWidth - 1, total - 1);

                using (var shadowPath = RoundedRect(
                           new Rectangle(Dpi.Scaled(1, _scale), Dpi.Scaled(2, _scale),
                                         body.Width, body.Height), _cornerRadius))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
                {
                    g.FillPath(shadowBrush, shadowPath);
                }

                using (var path = RoundedRect(body, _cornerRadius))
                {
                    using (var back = new SolidBrush(Color.FromArgb(246, 255, 255, 255)))
                    {
                        g.FillPath(back, path);
                    }
                    using (var border = new Pen(Color.FromArgb(48, stripe), Math.Max(1f, 1.2f * _scale)))
                    {
                        g.DrawPath(border, path);
                    }

                    var saved = g.Clip;
                    g.SetClip(path);
                    using (var accentBrush = new SolidBrush(stripe))
                    {
                        g.FillRectangle(accentBrush, 0, 0, stripeWidth, total);
                    }
                    g.Clip = saved;
                }

                int y = _paddingY;
                var layout = StringFormat.GenericTypographic;
                for (int i = 0; i < lines.Length; i++)
                {
                    y += Dpi.ScaledSpacing(lines[i].SpaceBefore, _scale);
                    using (var brush = new SolidBrush(lines[i].Color))
                    {
                        g.DrawString(lines[i].Text, lines[i].Font, brush,
                            new RectangleF(_paddingX, y, contentWidth, heights[i]), layout);
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
            var area = Screen.PrimaryScreen.WorkingArea;
            int x = area.Right - size.Width - _screenMargin;
            int y = area.Bottom - size.Height - _screenMargin;
            if (x < area.Left) x = area.Left + _screenMargin;
            if (y < area.Top) y = area.Top + _screenMargin;
            return new Point(x, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontTitle != null) _fontTitle.Dispose();
                if (_fontTime != null) _fontTime.Dispose();
                if (_fontBody != null) _fontBody.Dispose();
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