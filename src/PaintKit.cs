using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ImageView
{
    internal enum IconId
    {
        None = 0,
        ZoomIn,
        ZoomOut,
        Fit,
        Rotate,
        Resize,
        Filmstrip,
        More,
        Close,
        Maximize,
        Restore,
        Minimize,
        ChevronLeft,
        ChevronRight,
        ChevronDown,
        Image,
        Warning,
        Folder,
        Trash,
        Copy,
        Info,
        Wallpaper,
        External,
        Sliders,
        Check,
        Plus,
        Minus,
        DefaultApp
    }

    /// <summary>绘制工具箱：缓存画刷/画笔，提供圆角、阴影、文本、图标等原语。</summary>
    internal static class PaintKit
    {
        private static readonly Dictionary<int, SolidBrush> Brushes = new Dictionary<int, SolidBrush>();
        private static readonly Dictionary<long, Pen> Pens = new Dictionary<long, Pen>();

        internal static void Invalidate()
        {
            foreach (SolidBrush b in Brushes.Values)
            {
                b.Dispose();
            }
            Brushes.Clear();
            foreach (Pen p in Pens.Values)
            {
                p.Dispose();
            }
            Pens.Clear();
        }

        internal static SolidBrush Brush(Color color)
        {
            int key = color.ToArgb();
            SolidBrush brush;
            if (!Brushes.TryGetValue(key, out brush))
            {
                brush = new SolidBrush(color);
                Brushes[key] = brush;
            }
            return brush;
        }

        internal static Pen PenFor(Color color, float width)
        {
            int w = (int)Math.Round(width * 100f);
            long key = ((long)color.ToArgb() << 20) ^ w;
            Pen pen;
            if (!Pens.TryGetValue(key, out pen))
            {
                pen = new Pen(color, width);
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                Pens[key] = pen;
            }
            return pen;
        }

        // ---------- 形状 ----------

        internal static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            AddRoundRect(path, r, radius);
            return path;
        }

        internal static void AddRoundRect(GraphicsPath path, RectangleF r, float radius)
        {
            float max = Math.Min(r.Width, r.Height) / 2f;
            if (radius > max)
            {
                radius = max;
            }
            if (radius < 0.5f)
            {
                path.AddRectangle(r);
                return;
            }
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180f, 90f);
            path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
        }

        internal static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            if (r.Width <= 0f || r.Height <= 0f)
            {
                return;
            }
            if (radius < 0.5f)
            {
                g.FillRectangle(Brush(color), r);
                return;
            }
            using (GraphicsPath path = RoundRect(r, radius))
            {
                g.FillPath(Brush(color), path);
            }
        }

        internal static void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width)
        {
            if (r.Width <= 0f || r.Height <= 0f)
            {
                return;
            }
            RectangleF inset = new RectangleF(r.X + width / 2f, r.Y + width / 2f, r.Width - width, r.Height - width);
            if (inset.Width <= 0f || inset.Height <= 0f)
            {
                return;
            }
            using (GraphicsPath path = RoundRect(inset, Math.Max(0f, radius - width / 2f)))
            {
                g.DrawPath(PenFor(color, width), path);
            }
        }

        internal static void FillRoundGradient(Graphics g, RectangleF r, float radius, Color top, Color bottom)
        {
            if (r.Width <= 0f || r.Height <= 0f)
            {
                return;
            }
            using (GraphicsPath path = RoundRect(r, radius))
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new RectangleF(r.X, r.Y - 0.5f, r.Width, r.Height + 1f), top, bottom, LinearGradientMode.Vertical))
            {
                g.FillPath(lg, path);
            }
        }

        /// <summary>便宜的“软阴影”：用几层逐渐扩大的圆角矩形叠出扩散感。</summary>
        internal static void Shadow(Graphics g, RectangleF r, float radius, int spread, int baseAlpha)
        {
            if (spread <= 0)
            {
                return;
            }
            for (int i = spread; i >= 1; i--)
            {
                int alpha = baseAlpha / (i + 1);
                if (alpha <= 0)
                {
                    continue;
                }
                RectangleF rr = new RectangleF(r.X - i, r.Y - i + 1, r.Width + i * 2, r.Height + i * 2);
                FillRound(g, rr, radius + i, Color.FromArgb(alpha, 0, 0, 0));
            }
        }

        internal static void Checker(Graphics g, RectangleF r, int cell, Color a, Color b)
        {
            using (SolidBrush ba = new SolidBrush(a))
            using (SolidBrush bb = new SolidBrush(b))
            {
                int cols = (int)Math.Ceiling(r.Width / cell) + 1;
                int rows = (int)Math.Ceiling(r.Height / cell) + 1;
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < cols; x++)
                    {
                        SolidBrush brush = ((x + y) % 2 == 0) ? ba : bb;
                        float px = r.X + x * cell;
                        float py = r.Y + y * cell;
                        float w = Math.Min(cell, r.Right - px);
                        float h = Math.Min(cell, r.Bottom - py);
                        if (w > 0f && h > 0f)
                        {
                            g.FillRectangle(brush, px, py, w, h);
                        }
                    }
                }
            }
        }

        // ---------- 文本 ----------

        internal static void Text(Graphics g, string text, Font font, Color color, RectangleF r,
            StringAlignment hAlign, StringAlignment vAlign, bool ellipsis)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                fmt.Alignment = hAlign;
                fmt.LineAlignment = vAlign;
                fmt.Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None;
                fmt.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
                g.DrawString(text, font, Brush(color), r, fmt);
            }
        }

        internal static void Text(Graphics g, string text, Font font, Color color, RectangleF r)
        {
            Text(g, text, font, color, r, StringAlignment.Near, StringAlignment.Center, false);
        }

        internal static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text))
            {
                return SizeF.Empty;
            }
            using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                fmt.FormatFlags = StringFormatFlags.NoWrap;
                return g.MeasureString(text, font, new SizeF(100000f, 100000f), fmt);
            }
        }

        internal static float MeasureWidth(Graphics g, string text, Font font)
        {
            return Measure(g, text, font).Width;
        }

        // ---------- 图标 ----------

        /// <summary>在 16 单位虚拟坐标系里画矢量图标，可无损缩放。</summary>
        internal static void Icon(Graphics g, IconId id, RectangleF box, Color color, float weight)
        {
            if (id == IconId.None || box.Width <= 0f || box.Height <= 0f)
            {
                return;
            }
            float size = Math.Min(box.Width, box.Height);
            if (size < 2f)
            {
                return;
            }
            float ox = box.X + (box.Width - size) / 2f;
            float oy = box.Y + (box.Height - size) / 2f;
            float k = size / 16f;
            float pw = Math.Max(0.75f / k, weight / k);

            GraphicsState state = g.Save();
            try
            {
                g.TranslateTransform(ox, oy);
                g.ScaleTransform(k, k);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Pen pen = PenFor(color, pw);
                SolidBrush brush = Brush(color);

                switch (id)
                {
                    case IconId.ZoomIn:
                        g.DrawEllipse(pen, 2.4f, 2.4f, 8.4f, 8.4f);
                        g.DrawLine(pen, 10.2f, 10.2f, 13.9f, 13.9f);
                        g.DrawLine(pen, 4.6f, 6.6f, 8.6f, 6.6f);
                        g.DrawLine(pen, 6.6f, 4.6f, 6.6f, 8.6f);
                        break;

                    case IconId.ZoomOut:
                        g.DrawEllipse(pen, 2.4f, 2.4f, 8.4f, 8.4f);
                        g.DrawLine(pen, 10.2f, 10.2f, 13.9f, 13.9f);
                        g.DrawLine(pen, 4.6f, 6.6f, 8.6f, 6.6f);
                        break;

                    case IconId.Fit:
                        g.DrawLines(pen, new PointF[] { new PointF(2.6f, 6.2f), new PointF(2.6f, 2.6f), new PointF(6.2f, 2.6f) });
                        g.DrawLines(pen, new PointF[] { new PointF(9.8f, 2.6f), new PointF(13.4f, 2.6f), new PointF(13.4f, 6.2f) });
                        g.DrawLines(pen, new PointF[] { new PointF(13.4f, 9.8f), new PointF(13.4f, 13.4f), new PointF(9.8f, 13.4f) });
                        g.DrawLines(pen, new PointF[] { new PointF(6.2f, 13.4f), new PointF(2.6f, 13.4f), new PointF(2.6f, 9.8f) });
                        break;

                    case IconId.Rotate:
                        g.DrawArc(pen, 2.6f, 2.6f, 10.8f, 10.8f, -70f, 285f);
                        g.DrawLines(pen, new PointF[] { new PointF(11.0f, 1.3f), new PointF(12.4f, 4.2f), new PointF(9.2f, 4.4f) });
                        break;

                    case IconId.Resize:
                        g.DrawRectangle(pen, 2.2f, 2.2f, 11.6f, 11.6f);
                        g.DrawLine(pen, 5.4f, 10.6f, 10.6f, 5.4f);
                        g.DrawLines(pen, new PointF[] { new PointF(7.9f, 4.4f), new PointF(11.6f, 4.4f), new PointF(11.6f, 8.1f) });
                        g.DrawLines(pen, new PointF[] { new PointF(8.1f, 11.6f), new PointF(4.4f, 11.6f), new PointF(4.4f, 7.9f) });
                        break;

                    case IconId.Filmstrip:
                        g.DrawRectangle(pen, 1.5f, 4.2f, 4.0f, 7.6f);
                        g.DrawRectangle(pen, 6.4f, 4.2f, 3.2f, 7.6f);
                        g.DrawRectangle(pen, 10.4f, 4.2f, 4.1f, 7.6f);
                        break;

                    case IconId.More:
                        g.FillEllipse(brush, 6.6f, 2.6f, 2.8f, 2.8f);
                        g.FillEllipse(brush, 6.6f, 6.6f, 2.8f, 2.8f);
                        g.FillEllipse(brush, 6.6f, 10.6f, 2.8f, 2.8f);
                        break;

                    case IconId.Close:
                        g.DrawLine(pen, 4.2f, 4.2f, 11.8f, 11.8f);
                        g.DrawLine(pen, 11.8f, 4.2f, 4.2f, 11.8f);
                        break;

                    case IconId.Maximize:
                        g.DrawRectangle(pen, 3.4f, 3.4f, 9.2f, 9.2f);
                        break;

                    case IconId.Restore:
                        g.DrawRectangle(pen, 5.2f, 2.8f, 8.0f, 8.0f);
                        g.DrawLines(pen, new PointF[] { new PointF(2.8f, 5.2f), new PointF(2.8f, 13.2f), new PointF(10.8f, 13.2f), new PointF(10.8f, 10.8f) });
                        break;

                    case IconId.Minimize:
                        g.DrawLine(pen, 3.6f, 8f, 12.4f, 8f);
                        break;

                    case IconId.ChevronLeft:
                        g.DrawLines(pen, new PointF[] { new PointF(9.8f, 3.4f), new PointF(5.4f, 8f), new PointF(9.8f, 12.6f) });
                        break;

                    case IconId.ChevronRight:
                        g.DrawLines(pen, new PointF[] { new PointF(6.2f, 3.4f), new PointF(10.6f, 8f), new PointF(6.2f, 12.6f) });
                        break;

                    case IconId.ChevronDown:
                        g.DrawLines(pen, new PointF[] { new PointF(3.6f, 6.2f), new PointF(8f, 10.6f), new PointF(12.4f, 6.2f) });
                        break;

                    case IconId.Image:
                        g.DrawRectangle(pen, 2.2f, 3.4f, 11.6f, 9.2f);
                        g.FillEllipse(brush, 4.6f, 5.4f, 2.0f, 2.0f);
                        g.DrawLines(pen, new PointF[] { new PointF(3.4f, 11.4f), new PointF(7.2f, 7.6f), new PointF(10.0f, 10.6f), new PointF(11.6f, 9.0f), new PointF(12.6f, 10.0f) });
                        break;

                    case IconId.Warning:
                        g.DrawLines(pen, new PointF[] { new PointF(8f, 2.2f), new PointF(14.2f, 13.2f), new PointF(1.8f, 13.2f), new PointF(8f, 2.2f) });
                        g.DrawLine(pen, 8f, 6.4f, 8f, 9.6f);
                        g.FillEllipse(brush, 7.2f, 10.6f, 1.6f, 1.6f);
                        break;

                    case IconId.Folder:
                        g.DrawLines(pen, new PointF[] { new PointF(1.8f, 12.6f), new PointF(1.8f, 3.6f), new PointF(6.4f, 3.6f), new PointF(7.8f, 5.4f), new PointF(14.2f, 5.4f), new PointF(14.2f, 12.6f), new PointF(1.8f, 12.6f) });
                        break;

                    case IconId.Trash:
                        g.DrawLine(pen, 1.8f, 4.4f, 14.2f, 4.4f);
                        g.DrawLines(pen, new PointF[] { new PointF(5.2f, 4.4f), new PointF(5.2f, 2.4f), new PointF(10.8f, 2.4f), new PointF(10.8f, 4.4f) });
                        g.DrawLines(pen, new PointF[] { new PointF(3.4f, 4.4f), new PointF(4.4f, 13.8f), new PointF(11.6f, 13.8f), new PointF(12.6f, 4.4f) });
                        g.DrawLine(pen, 6.6f, 7.2f, 6.6f, 11.4f);
                        g.DrawLine(pen, 9.4f, 7.2f, 9.4f, 11.4f);
                        break;

                    case IconId.Copy:
                        g.DrawRectangle(pen, 2.4f, 2.4f, 8.2f, 8.2f);
                        g.DrawLines(pen, new PointF[] { new PointF(5.4f, 13.6f), new PointF(13.6f, 13.6f), new PointF(13.6f, 5.4f) });
                        break;

                    case IconId.Info:
                        g.DrawEllipse(pen, 2.2f, 2.2f, 11.6f, 11.6f);
                        g.FillEllipse(brush, 7.1f, 4.4f, 1.8f, 1.8f);
                        g.DrawLine(pen, 8f, 7.4f, 8f, 11.6f);
                        break;

                    case IconId.Wallpaper:
                        g.DrawRectangle(pen, 1.8f, 3.2f, 12.4f, 9.6f);
                        g.DrawLines(pen, new PointF[] { new PointF(3.2f, 11.4f), new PointF(6.8f, 7.0f), new PointF(9.6f, 10.2f), new PointF(11.2f, 8.4f), new PointF(12.8f, 10.2f) });
                        g.DrawLine(pen, 8f, 14.8f, 8f, 13.2f);
                        break;

                    case IconId.External:
                        g.DrawLines(pen, new PointF[] { new PointF(8.6f, 2.6f), new PointF(2.6f, 2.6f), new PointF(2.6f, 13.4f), new PointF(13.4f, 13.4f), new PointF(13.4f, 7.4f) });
                        g.DrawLine(pen, 8.4f, 7.6f, 13.8f, 2.2f);
                        g.DrawLines(pen, new PointF[] { new PointF(9.6f, 2.2f), new PointF(13.8f, 2.2f), new PointF(13.8f, 6.4f) });
                        break;

                    case IconId.Sliders:
                        g.DrawLine(pen, 1.8f, 4.4f, 14.2f, 4.4f);
                        g.DrawLine(pen, 1.8f, 8.0f, 14.2f, 8.0f);
                        g.DrawLine(pen, 1.8f, 11.6f, 14.2f, 11.6f);
                        g.FillEllipse(brush, 4.2f, 2.9f, 3.0f, 3.0f);
                        g.FillEllipse(brush, 9.4f, 6.5f, 3.0f, 3.0f);
                        g.FillEllipse(brush, 5.6f, 10.1f, 3.0f, 3.0f);
                        break;

                    case IconId.Check:
                        g.DrawLines(pen, new PointF[] { new PointF(3.2f, 8.4f), new PointF(6.4f, 11.6f), new PointF(12.8f, 4.6f) });
                        break;

                    case IconId.Plus:
                        g.DrawLine(pen, 8f, 3.4f, 8f, 12.6f);
                        g.DrawLine(pen, 3.4f, 8f, 12.6f, 8f);
                        break;

                    case IconId.Minus:
                        g.DrawLine(pen, 3.4f, 8f, 12.6f, 8f);
                        break;

                    case IconId.DefaultApp:
                        g.DrawEllipse(pen, 2.2f, 5.2f, 5.2f, 5.2f);
                        g.DrawEllipse(pen, 8.6f, 5.2f, 5.2f, 5.2f);
                        g.DrawLine(pen, 5.0f, 7.8f, 11.0f, 7.8f);
                        break;
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// 标题栏左侧的应用标记 —— 与应用图标（tools/make-icon.py → assets/app.ico）**同一套几何**：
        /// 蓝色渐变圆角方块 + 白色圆角照片卡片 + 卡片里挖出蓝色山峦与太阳。
        /// 坐标都用 16 单位，按实际尺寸缩放，所以 16px 的图标和 34px 的标记形状完全一致。
        /// </summary>
        internal static void AppGlyph(Graphics g, RectangleF box, Color accent, Color ink)
        {
            float size = Math.Min(box.Width, box.Height);
            if (size < 6f)
            {
                return;
            }
            RectangleF r = new RectangleF(box.X + (box.Width - size) / 2f, box.Y + (box.Height - size) / 2f, size, size);

            float k = size / 16f;
            GraphicsState st = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(r.X, r.Y);
                g.ScaleTransform(k, k);

                // 底：渐变圆角方块
                Color top = Color.FromArgb(Math.Min(255, accent.R + 34), Math.Min(255, accent.G + 30),
                    Math.Min(255, accent.B + 45));
                FillRoundGradient(g, new RectangleF(0f, 0f, 16f, 16f), 3.70f, top, accent);

                // 白色照片卡片
                RectangleF card = new RectangleF(3.00f, 4.30f, 10.00f, 7.40f);
                using (GraphicsPath cardPath = RoundRect(card, 1.10f))
                {
                    using (SolidBrush paper = new SolidBrush(Color.FromArgb(255, 255, 255)))
                    {
                        g.FillPath(paper, cardPath);
                    }

                    // 卡片里的负空间：山峦 + 太阳，用卡片路径裁切
                    GraphicsState clipState = g.Save();
                    try
                    {
                        g.SetClip(cardPath, CombineMode.Intersect);
                        using (SolidBrush hole = new SolidBrush(accent))
                        {
                            g.FillPolygon(hole, new PointF[]
                            {
                                new PointF(6.90f, 11.70f), new PointF(9.60f, 7.80f), new PointF(12.20f, 11.70f)
                            });
                            g.FillPolygon(hole, new PointF[]
                            {
                                new PointF(3.00f, 11.70f), new PointF(6.90f, 6.90f), new PointF(9.20f, 11.70f)
                            });
                            g.FillEllipse(hole, 9.88f, 5.68f, 1.24f, 1.24f);
                        }
                    }
                    finally
                    {
                        g.Restore(clipState);
                    }
                }
            }
            finally
            {
                g.Restore(st);
            }
        }
    }
}
