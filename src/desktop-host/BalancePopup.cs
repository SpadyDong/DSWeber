using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class BalancePopup : Form
    {
        private const int BasePopupWidth = 248;
        private const int BasePaddingX = 16;
        private const int BasePaddingY = 14;
        private const int BaseGap = 10;
        private const int BaseCornerRadius = 10;

        private static readonly Color Accent = Color.FromArgb(77, 107, 254);
        private static readonly Color ErrorColor = Color.FromArgb(214, 69, 69);
        private static readonly Color TextPrimary = Color.FromArgb(32, 34, 40);
        private static readonly Color TextMuted = Color.FromArgb(130, 134, 145);
        private static readonly Color PeakColor = Color.FromArgb(214, 69, 69);
        private static readonly Color ValleyColor = Color.FromArgb(64, 158, 96);

        private float _scale = -1f;
        private int _popupWidth;
        private int _paddingX;
        private int _paddingY;
        private int _gap;
        private int _cornerRadius;
        private Font _fontTitle;
        private Font _fontBody;
        private Font _fontSmall;

        private Point _anchor;
        private int _session;
        private BalanceResult _balance;
        private CodexQuota _codex;
        private bool _codexPending;
        private ArkPlan _ark;
        private bool _arkPending;

        public BalancePopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            UpdateScale();
            Size = new Size(_popupWidth, 80);

            MouseUp += delegate { HidePopup(); };
        }

        /// <summary>按气泡所在显示器的 DPI 重排尺寸与字体。</summary>
        private void UpdateScale()
        {
            float scale = Dpi.ScaleForScreen(Screen.FromPoint(_anchor));
            if (Math.Abs(scale - _scale) < 0.001f) return;

            _scale = scale;
            _popupWidth = Dpi.Scaled(BasePopupWidth, scale);
            _paddingX = Dpi.Scaled(BasePaddingX, scale);
            _paddingY = Dpi.Scaled(BasePaddingY, scale);
            _gap = Dpi.Scaled(BaseGap, scale);
            _cornerRadius = Dpi.Scaled(BaseCornerRadius, scale);

            if (_fontTitle != null) _fontTitle.Dispose();
            if (_fontBody != null) _fontBody.Dispose();
            if (_fontSmall != null) _fontSmall.Dispose();

            _fontTitle = new Font("Microsoft YaHei UI", 9f * scale, FontStyle.Regular);
            _fontBody = new Font("Microsoft YaHei UI", 9f * scale, FontStyle.Regular);
            _fontSmall = new Font("Microsoft YaHei UI", 8f * scale, FontStyle.Regular);
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

        public int ShowLoading(Rectangle ballBounds, bool codexEnabled,
                              BalanceResult cachedBalance, CodexQuota cachedCodex, ArkPlan cachedArk)
        {
            _session++;
            _anchor = new Point(ballBounds.Right, ballBounds.Top + ballBounds.Height / 2);
            UpdateScale();
            _balance = cachedBalance;
            _codex = codexEnabled ? cachedCodex : null;
            _codexPending = codexEnabled && cachedCodex == null;
            _ark = cachedArk;
            _arkPending = cachedArk == null;
            Render(BuildSurface());
            if (!Visible) Show();
            return _session;
        }

        public void SetBalance(int session, BalanceResult result)
        {
            if (result == null) return;
            if (session != _session) return;

            _balance = result;
            Render(BuildSurface());
            if (!Visible) Show();
        }

        public void SetCodex(int session, CodexQuota quota)
        {
            if (quota == null) return;
            if (session != _session) return;

            _codex = quota;
            _codexPending = false;
            Render(BuildSurface());
            if (!Visible) Show();
        }

        public void SetArk(int session, ArkPlan plan)
        {
            if (plan == null) return;
            if (session != _session) return;

            _ark = plan;
            _arkPending = false;
            Render(BuildSurface());
            if (!Visible) Show();
        }

        public void Follow(Rectangle ballBounds)
        {
            _anchor = new Point(ballBounds.Right, ballBounds.Top + ballBounds.Height / 2);
            if (!Visible) return;

            float before = _scale;
            UpdateScale();
            if (Math.Abs(_scale - before) > 0.001f)
            {
                // 跨到了 DPI 不同的显示器，整块重绘
                Render(BuildSurface());
                return;
            }

            var target = Reposition(Size);
            if (target != Location) Location = target;
        }

        public void HidePopup()
        {
            _session++;
            _balance = null;
            _codex = null;
            _codexPending = false;
            _ark = null;
            _arkPending = false;
            if (Visible) Hide();
        }

        private Bitmap BuildSurface()
        {
            var lines = new List<TextLine>();

            if (_balance == null)
            {
                lines.Add(new TextLine("DeepSeek 余额", _fontTitle, TextMuted, 0));
                lines.Add(new TextLine("正在查询…", _fontBody, TextPrimary, 6));
            }
            else if (_balance.Ok)
            {
                string symbol = string.Equals(_balance.Currency, "CNY", StringComparison.OrdinalIgnoreCase) ? "¥"
                              : string.Equals(_balance.Currency, "USD", StringComparison.OrdinalIgnoreCase) ? "$"
                              : "";

                string amount = symbol + (string.IsNullOrEmpty(_balance.TotalBalance) ? "-" : _balance.TotalBalance);

                PricingSlot slot = PricingCalendar.CurrentSlot();
                string slotText = PricingCalendar.SlotText(slot);

                string title = "DeepSeek 余额";
                if (!string.IsNullOrEmpty(slotText))
                    title += "（" + slotText + "）";

                lines.Add(new TextLine(title, _fontTitle, TextMuted, 0));
                lines.Add(new TextLine("余额", _fontBody, TextPrimary, 4,
                    amount, _fontBody, BalanceColor(_balance.TotalBalance)));
            }
            else
            {
                lines.Add(new TextLine("DeepSeek 余额", _fontTitle, TextMuted, 0));
                lines.Add(new TextLine(_balance.ErrorText, _fontBody, ErrorColor, 6));
                if (!string.IsNullOrEmpty(_balance.ErrorDetail))
                    lines.Add(new TextLine(_balance.ErrorDetail, _fontSmall, TextMuted, 5));
            }

            if (_codexPending || _codex != null)
            {
                lines.Add(TextLine.Rule(10));

                string head = "Codex 配额";
                if (_codex != null && _codex.Ok && !string.IsNullOrEmpty(_codex.PlanType))
                    head += "（" + PlanLabel(_codex.PlanType) + "）";
                lines.Add(new TextLine(head, _fontTitle, TextMuted, 6));

                if (_codexPending)
                {
                    lines.Add(new TextLine("正在查询…", _fontBody, TextPrimary, 4));
                }
                else if (_codex.Ok)
                {
                    if (_codex.Windows != null)
                    {
                        foreach (var window in _codex.Windows)
                        {
                            lines.Add(new TextLine(window.Label, _fontBody, TextPrimary, 4,
                                "剩余 " + window.LeftPercent + "%",
                                _fontBody, LeftColor(window.LeftPercent),
                                ResetSuffix(window.ResetsAtLocal), _fontBody, TextMuted));
                        }
                    }
                }
                else
                {
                    lines.Add(new TextLine(_codex.ErrorText, _fontBody, ErrorColor, 4));
                    if (!string.IsNullOrEmpty(_codex.ErrorDetail))
                        lines.Add(new TextLine(_codex.ErrorDetail, _fontSmall, TextMuted, 5));
                }
            }

            if (_arkPending || _ark != null)
            {
                lines.Add(TextLine.Rule(10));
                lines.Add(new TextLine("火山方舟（coding plan）", _fontTitle, TextMuted, 6));

                if (_arkPending)
                {
                    lines.Add(new TextLine("正在查询…", _fontBody, TextPrimary, 4));
                }
                else if (_ark.Ok)
                {
                    if (_ark.SessionLeftPercent != null)
                    {
                        lines.Add(new TextLine("5 小时", _fontBody, TextPrimary, 4,
                            "剩余 " + _ark.SessionLeftPercent.Value + "%",
                            _fontBody, LeftColor(_ark.SessionLeftPercent.Value)));
                    }
                    if (_ark.MonthlyLeftPercent != null)
                    {
                        lines.Add(new TextLine("一个月", _fontBody, TextPrimary, 4,
                            "剩余 " + _ark.MonthlyLeftPercent.Value + "%",
                            _fontBody, LeftColor(_ark.MonthlyLeftPercent.Value)));
                    }
                }
                else
                {
                    lines.Add(new TextLine(_ark.ErrorText, _fontBody, ErrorColor, 4));
                    if (!string.IsNullOrEmpty(_ark.ErrorDetail))
                        lines.Add(new TextLine(_ark.ErrorDetail, _fontSmall, TextMuted, 5));
                }
            }

            Color stripe = (_balance != null && !_balance.Ok) ? ErrorColor : Accent;
            return Compose(lines.ToArray(), stripe);
        }

        private static Color LeftColor(int leftPercent)
        {
            return leftPercent >= 10 ? ValleyColor : PeakColor;
        }

        private static string ResetSuffix(DateTime? resetsAtLocal)
        {
            if (resetsAtLocal == null) return null;

            var reset = resetsAtLocal.Value;
            string text = reset.Date == DateTime.Now.Date
                ? reset.ToString("HH:mm")
                : reset.ToString("M'月'd'日'");
            return "（" + text + "重置）";
        }

        private static Color BalanceColor(string totalBalance)
        {
            double value;
            if (!double.TryParse(totalBalance, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return TextMuted;

            return value >= 10 ? ValleyColor : PeakColor;
        }

        private static string PlanLabel(string planType)
        {
            switch (planType)
            {
                case "free": return "Free";
                case "go": return "Go";
                case "plus": return "Plus";
                case "pro": return "Pro";
                case "prolite": return "Pro Lite";
                case "team": return "Team";
                case "business": return "Business";
                case "enterprise": return "Enterprise";
                case "edu": return "Edu";
                case "edu_plus": return "Edu Plus";
                case "edu_pro": return "Edu Pro";
                default: return planType;
            }
        }

        private sealed class TextLine
        {
            public readonly string Text;
            public readonly Font Font;
            public readonly Color Color;
            public readonly int SpaceBefore;
            public readonly string RightText;
            public readonly Font RightFont;
            public readonly Color RightColor;
            public readonly string Suffix;
            public readonly Font SuffixFont;
            public readonly Color SuffixColor;
            public readonly bool Divider;

            public TextLine(string text, Font font, Color color, int spaceBefore)
            {
                Text = text == null ? "" : text;
                Font = font;
                Color = color;
                SpaceBefore = spaceBefore;
                RightFont = font;
                RightColor = color;
            }

            public TextLine(string text, Font font, Color color, int spaceBefore,
                            string rightText, Font rightFont, Color rightColor)
            {
                Text = text == null ? "" : text;
                Font = font;
                Color = color;
                SpaceBefore = spaceBefore;
                RightText = string.IsNullOrEmpty(rightText) ? null : rightText;
                RightFont = rightFont == null ? font : rightFont;
                RightColor = rightColor;
            }

            public TextLine(string text, Font font, Color color, int spaceBefore,
                            string rightText, Font rightFont, Color rightColor,
                            string suffix, Font suffixFont, Color suffixColor)
            {
                Text = text == null ? "" : text;
                Font = font;
                Color = color;
                SpaceBefore = spaceBefore;
                RightText = string.IsNullOrEmpty(rightText) ? null : rightText;
                RightFont = rightFont == null ? font : rightFont;
                RightColor = rightColor;
                Suffix = string.IsNullOrEmpty(suffix) ? null : suffix;
                SuffixFont = suffixFont == null ? font : suffixFont;
                SuffixColor = suffixColor;
            }

            private TextLine(int spaceBefore)
            {
                Divider = true;
                Text = "";
                SpaceBefore = spaceBefore;
            }

            public static TextLine Rule(int spaceBefore)
            {
                return new TextLine(spaceBefore);
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
                    if (lines[i].Divider)
                    {
                        heights[i] = Dpi.Scaled(1, _scale);
                    }
                    else
                    {
                        var size = g.MeasureString(lines[i].Text, lines[i].Font, contentWidth);
                        heights[i] = (int)Math.Ceiling(size.Height);

                        if (lines[i].RightText != null)
                        {
                            var rightSize = g.MeasureString(lines[i].RightText, lines[i].RightFont, contentWidth);
                            int rightHeight = (int)Math.Ceiling(rightSize.Height);
                            if (rightHeight > heights[i]) heights[i] = rightHeight;
                        }

                        if (lines[i].Suffix != null)
                        {
                            var suffixSize = g.MeasureString(lines[i].Suffix, lines[i].SuffixFont, contentWidth);
                            int suffixHeight = (int)Math.Ceiling(suffixSize.Height);
                            if (suffixHeight > heights[i]) heights[i] = suffixHeight;
                        }
                    }
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

                    if (lines[i].Divider)
                    {
                        using (var pen = new Pen(Color.FromArgb(30, 0, 0, 0), Math.Max(1f, 1f * _scale)))
                        {
                            g.DrawLine(pen, _paddingX, y + 0.5f, _popupWidth - _paddingX, y + 0.5f);
                        }
                        y += heights[i];
                        continue;
                    }

                    float leftX = _paddingX;
                    using (var brush = new SolidBrush(lines[i].Color))
                    {
                        g.DrawString(lines[i].Text, lines[i].Font, brush,
                            new RectangleF(leftX, y, contentWidth, heights[i]), layout);
                    }

                    if (lines[i].Suffix != null)
                    {
                        leftX += g.MeasureString(lines[i].Text, lines[i].Font,
                            contentWidth, layout).Width;
                        using (var brush = new SolidBrush(lines[i].SuffixColor))
                        {
                            g.DrawString(lines[i].Suffix, lines[i].SuffixFont, brush,
                                new RectangleF(leftX, y, contentWidth, heights[i]), layout);
                        }
                    }

                    if (lines[i].RightText != null)
                    {
                        var rightSize = g.MeasureString(lines[i].RightText, lines[i].RightFont,
                            contentWidth, layout);
                        float rightX = _paddingX + contentWidth - rightSize.Width;
                        using (var brush = new SolidBrush(lines[i].RightColor))
                        {
                            g.DrawString(lines[i].RightText, lines[i].RightFont, brush,
                                new RectangleF(rightX, y, rightSize.Width + Dpi.Scaled(2, _scale), heights[i]), layout);
                        }
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
            int ballSize = Dpi.Scaled(FloatingBall.BaseBallSize, _scale);

            int x = _anchor.X + _gap;
            if (x + size.Width > area.Right) x = _anchor.X - _gap - size.Width - ballSize;
            if (x < area.Left) x = area.Left + _gap;

            int y = _anchor.Y - size.Height / 2;
            if (y + size.Height > area.Bottom) y = area.Bottom - size.Height - _gap;
            if (y < area.Top) y = area.Top + _gap;

            return new Point(x, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontTitle != null) _fontTitle.Dispose();
                if (_fontBody != null) _fontBody.Dispose();
                if (_fontSmall != null) _fontSmall.Dispose();
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
