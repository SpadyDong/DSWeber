using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DshTray
{
    internal static class IconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon CreateGlyphIcon(int size, Color fill)
        {
            using (var bmp = CreateGlyphBitmap(size, fill))
            {
                return FromBitmapOwned(bmp);
            }
        }

        public static Bitmap CreateGlyphBitmap(int size, Color fill)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bmp.SetResolution(96f, 96f);

            var scale = size / BrandGlyph.ViewBoxWidth * 0.86f;
            var offsetX = (size - BrandGlyph.ViewBoxWidth * scale) / 2f;
            var offsetY = (size - BrandGlyph.ViewBoxHeight * scale) / 2f;

            using (var path = BuildGlyphPath())
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.TranslateTransform(offsetX, offsetY);
                g.ScaleTransform(scale, scale);
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }
            }

            return bmp;
        }

        private static Icon FromBitmapOwned(Bitmap bmp)
        {
            var handle = bmp.GetHicon();
            try
            {
                using (var shared = Icon.FromHandle(handle))
                {
                    return (Icon)shared.Clone();
                }
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        private static GraphicsPath BuildGlyphPath()
        {
            var path = new GraphicsPath(FillMode.Winding);
            float cx = 0f, cy = 0f, startX = 0f, startY = 0f;

            foreach (var seg in ParseSegments(BrandGlyph.PathData))
            {
                switch (seg.Command)
                {
                    case 'M':
                        for (int i = 0; i + 1 < seg.Args.Count; i += 2)
                        {
                            cx = seg.Args[i];
                            cy = seg.Args[i + 1];
                            if (i == 0)
                            {
                                path.StartFigure();
                                startX = cx;
                                startY = cy;
                            }
                        }
                        break;

                    case 'C':
                        for (int i = 0; i + 5 < seg.Args.Count; i += 6)
                        {
                            path.AddBezier(
                                cx, cy,
                                seg.Args[i], seg.Args[i + 1],
                                seg.Args[i + 2], seg.Args[i + 3],
                                seg.Args[i + 4], seg.Args[i + 5]);
                            cx = seg.Args[i + 4];
                            cy = seg.Args[i + 5];
                        }
                        break;

                    case 'Z':
                        path.CloseFigure();
                        cx = startX;
                        cy = startY;
                        break;
                }
            }

            return path;
        }

        private struct Segment
        {
            public char Command;
            public List<float> Args;
        }

        private static IEnumerable<Segment> ParseSegments(string d)
        {
            var segments = new List<Segment>();
            Segment current = new Segment { Command = '\0', Args = null };
            var token = new System.Text.StringBuilder();

            Action flushNumber = () =>
            {
                if (token.Length == 0) return;
                current.Args.Add(float.Parse(token.ToString(), CultureInfo.InvariantCulture));
                token.Length = 0;
            };

            for (int i = 0; i < d.Length; i++)
            {
                char c = d[i];

                if (c == 'M' || c == 'C' || c == 'Z')
                {
                    if (current.Command != '\0')
                    {
                        flushNumber();
                        segments.Add(current);
                    }
                    current = new Segment { Command = c, Args = new List<float>() };
                }
                else if (c == '-' && token.Length > 0 && token[token.Length - 1] != 'e' && token[token.Length - 1] != 'E')
                {
                    flushNumber();
                    token.Append(c);
                }
                else if (c == ' ' || c == ',' || c == '\r' || c == '\n' || c == '\t')
                {
                    flushNumber();
                }
                else
                {
                    token.Append(c);
                }
            }

            if (current.Command != '\0')
            {
                flushNumber();
                segments.Add(current);
            }

            return segments;
        }
    }
}