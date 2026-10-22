using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>
    /// 图片信息浮层：直方图 + 分组明细（文件 / 拍摄 / 位置）。
    /// 行数很多时可用滚轮滚动。
    /// </summary>
    internal sealed class InfoOverlay : Overlay
    {
        private sealed class Row
        {
            internal string Label;
            internal string Value;
            internal bool Header;
        }

        private readonly List<Row> _rows = new List<Row>();
        private Histogram _histogram;
        private ColorProfile _profile;
        private string _exifColorSpace;

        private Rectangle _card;
        private Rectangle _rowsRect;
        private readonly Rectangle[] _graphRects = new Rectangle[3];
        private Rectangle _closeRect;
        private int _contentHeight;
        private int _scroll;
        private bool _hotClose;

        internal InfoOverlay()
        {
        }

        internal void Fill(ImageDocument document, int index, int total)
        {
            if (document == null)
            {
                return;
            }
            _rows.Clear();
            _histogram = Histogram.Compute(document.Preview);
            _profile = ColorProfile.Read(document.FilePath);
            _exifColorSpace = document.Exif != null ? document.Exif.ColorSpace : null;

            Section("文件");
            Row2(Strings.InfoName, document.FileName);
            Row2(Strings.InfoPath, SafeDirectory(document.FilePath));
            string size = document.PixelWidth + " × " + document.PixelHeight;
            if (document.SourceWidth != document.PixelWidth || document.SourceHeight != document.PixelHeight)
            {
                size += "（原图 " + document.SourceWidth + " × " + document.SourceHeight + "，已按 EXIF 方向校正）";
            }
            Row2(Strings.InfoSize, size);
            Row2(Strings.InfoFileSize, FormatSize(document.FileSize));
            Row2(Strings.InfoFormat, document.FormatName);
            if (index >= 0 && total > 0)
            {
                Row2("序号", (index + 1) + " / " + total);
            }
            Row2(Strings.InfoModified, document.Modified.ToString("yyyy-MM-dd HH:mm"));

            string colorSpace = _profile != null ? _profile.Describe(_exifColorSpace) : _exifColorSpace;
            if (!string.IsNullOrEmpty(colorSpace))
            {
                Row2("色彩空间", colorSpace);
            }
            if (!_histogram.IsEmpty)
            {
                Row2("色调", "平均亮度 " + _histogram.MeanLuma.ToString("0") + " / 255");
            }

            ExifInfo exif = document.Exif;
            if (exif != null && exif.HasAny)
            {
                Section("拍摄");
                string device = (exif.Make + " " + exif.Model).Trim();
                if (!string.IsNullOrEmpty(device))
                {
                    Row2(Strings.InfoCamera, device);
                }
                if (!string.IsNullOrEmpty(exif.LensModel))
                {
                    Row2("镜头", exif.LensModel);
                }
                if (!string.IsNullOrEmpty(exif.LensSpecification))
                {
                    Row2("镜头规格", exif.LensSpecification);
                }
                if (!string.IsNullOrEmpty(exif.DateTimeOriginal))
                {
                    Row2(Strings.InfoTaken, exif.DateTimeOriginal);
                }
                if (!string.IsNullOrEmpty(exif.Exposure))
                {
                    Row2(Strings.InfoExposure, exif.Exposure);
                }
                if (!string.IsNullOrEmpty(exif.Aperture))
                {
                    Row2(Strings.InfoAperture, exif.Aperture);
                }
                if (!string.IsNullOrEmpty(exif.Iso))
                {
                    Row2(Strings.InfoIso, exif.Iso.Replace("ISO ", string.Empty));
                }
                if (!string.IsNullOrEmpty(exif.FocalLength))
                {
                    string focal = exif.FocalLength;
                    if (!string.IsNullOrEmpty(exif.FocalLength35mm))
                    {
                        focal += "（" + exif.FocalLength35mm + "）";
                    }
                    Row2(Strings.InfoFocal, focal);
                }
                if (!string.IsNullOrEmpty(exif.ExposureBias))
                {
                    Row2("曝光补偿", exif.ExposureBias);
                }
                if (!string.IsNullOrEmpty(exif.ExposureProgram))
                {
                    Row2("曝光程序", exif.ExposureProgram);
                }
                if (!string.IsNullOrEmpty(exif.MeteringMode))
                {
                    Row2("测光", exif.MeteringMode);
                }
                if (!string.IsNullOrEmpty(exif.Flash))
                {
                    Row2("闪光灯", exif.Flash);
                }
                if (!string.IsNullOrEmpty(exif.WhiteBalance))
                {
                    Row2("白平衡", exif.WhiteBalance);
                }
                if (!string.IsNullOrEmpty(exif.SceneCaptureType))
                {
                    Row2("场景", exif.SceneCaptureType);
                }
                if (!string.IsNullOrEmpty(exif.Software))
                {
                    Row2("软件", exif.Software);
                }
                if (!string.IsNullOrEmpty(exif.Artist))
                {
                    Row2("作者", exif.Artist);
                }
                if (exif.Orientation != 1)
                {
                    Row2(Strings.InfoOrient, "EXIF " + exif.Orientation + "（已自动校正）");
                }

                if (exif.HasGps)
                {
                    Section("位置");
                    if (!string.IsNullOrEmpty(exif.GpsLatitudeText))
                    {
                        Row2("纬度", exif.GpsLatitudeText);
                    }
                    if (!string.IsNullOrEmpty(exif.GpsLongitudeText))
                    {
                        Row2("经度", exif.GpsLongitudeText);
                    }
                    if (!string.IsNullOrEmpty(exif.GpsAltitudeText))
                    {
                        Row2("海拔", exif.GpsAltitudeText);
                    }
                    if (!string.IsNullOrEmpty(exif.GpsTimeText))
                    {
                        Row2("定位时间", exif.GpsTimeText);
                    }
                }
            }
        }

        private void Section(string title)
        {
            Row row = new Row();
            row.Header = true;
            row.Label = title;
            _rows.Add(row);
        }

        private void Row2(string label, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            Row row = new Row();
            row.Label = label;
            row.Value = value;
            _rows.Add(row);
        }

        private static string SafeDirectory(string path)
        {
            try
            {
                return System.IO.Path.GetDirectoryName(path);
            }
            catch (Exception)
            {
                return path;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024)
            {
                return (bytes / 1048576.0).ToString("0.0") + " MB";
            }
            if (bytes >= 1024)
            {
                return (bytes / 1024.0).ToString("0") + " KB";
            }
            return bytes + " B";
        }

        // ---------------- 布局 ----------------

        private const int RowHeight = 24;
        private const int SectionHeight = 30;
        private const int GraphHeight = 152;
        private const int GraphChartHeight = 86;

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            Rectangle viewport = form.GetImageArea();
            int width = Ui.S(780);
            int height = Math.Min(Ui.S(680), viewport.Height - Ui.S(30));
            _card = OverlayPaint.CenterCard(client, width, Math.Max(Ui.S(420), height));

            int pad = Ui.S(22);
            int gap = Ui.S(12);
            int contentTop = _card.Y + Ui.S(54);
            int graphWidth = (_card.Width - pad * 2 - gap * 2) / 3;
            for (int i = 0; i < 3; i++)
            {
                _graphRects[i] = new Rectangle(_card.X + pad + i * (graphWidth + gap), contentTop, graphWidth,
                    Ui.S(GraphHeight));
            }
            int graphsBottom = contentTop + Ui.S(GraphHeight);
            int rowsTop = graphsBottom + Ui.S(10);
            int rowsHeight = Math.Max(Ui.S(60), _card.Bottom - Ui.S(64) - rowsTop);
            // 行高对齐：底部不会出现半行被按钮硬切的效果
            int unit = Ui.S(RowHeight);
            rowsHeight -= rowsHeight % unit;
            _rowsRect = new Rectangle(_card.X + pad, rowsTop, _card.Width - pad * 2, rowsHeight);

            _contentHeight = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                _contentHeight += _rows[i].Header ? Ui.S(SectionHeight) : Ui.S(RowHeight);
            }
            ClampScroll();

            _closeRect = new Rectangle(_card.Right - pad - Ui.S(96), _card.Bottom - pad - Ui.S(34), Ui.S(96),
                Ui.S(34));
        }

        private void ClampScroll()
        {
            int max = Math.Max(0, _contentHeight - _rowsRect.Height);
            if (_scroll < 0)
            {
                _scroll = 0;
            }
            if (_scroll > max)
            {
                _scroll = max;
            }
        }

        private bool Scrollable
        {
            get { return _contentHeight > _rowsRect.Height; }
        }

        internal override void Detach(ViewerForm form)
        {
            if (_histogram != null)
            {
                _histogram.Dispose();
            }
        }

        internal override bool HandleWheel(int delta, Point point, ViewerForm form)
        {
            if (!_card.Contains(point) || !Scrollable)
            {
                return false;
            }
            _scroll -= delta / 2;
            ClampScroll();
            return true;
        }

        // ---------------- 绘制 ----------------

        internal override void Paint(Graphics g, ViewerForm form)
        {
            OverlayPaint.Scrim(g, form);
            if (_card.IsEmpty)
            {
                Layout(form, form.ClientRectangle);
            }
            OverlayPaint.Card(g, _card, Strings.InfoTitle, null);

            PaintGraphs(g);
            PaintRows(g);
            PaintRowsFade(g);

            // 行区与底部按钮之间的分隔线
            Palette divider = Theme.P;
            int dividerY = _rowsRect.Bottom + Ui.S(6);
            g.DrawLine(PaintKit.PenFor(Color.FromArgb(divider.Dark ? 70 : 90, divider.Border), 1f),
                _card.X + Ui.S(22), dividerY, _card.Right - Ui.S(22), dividerY);

            if (Scrollable)
            {
                PaintScrollBar(g);
            }

            OverlayPaint.Button(g, _closeRect, Strings.InfoClose, true, _hotClose, false);
        }

        private void PaintGraphs(Graphics g)
        {
            PaintPanelChrome(g, _graphRects[0], "直方图", "左暗右亮\n两端堆高=死黑/死白", false);
            PaintPanelChrome(g, _graphRects[1], "波形图", "横=画面左右 · 竖=亮度\n触顶=过曝 · 触底=死黑", false);
            PaintPanelChrome(g, _graphRects[2], "RGB 分量", "三条整体错开\n说明偏色/白平衡不准", true);

            if (_histogram == null || _histogram.IsEmpty)
            {
                Palette empty = Theme.P;
                PaintKit.Text(g, "无法统计", Fonts.Small, empty.TextDim, ChartRect(_graphRects[0]),
                    StringAlignment.Center, StringAlignment.Center, false);
                return;
            }

            PaintHistogramChart(g, ChartRect(_graphRects[0]));
            PaintWaveformChart(g, ChartRect(_graphRects[1]));
            PaintChannelsChart(g, ChartRect(_graphRects[2]));
        }

        private static Rectangle ChartRect(Rectangle panel)
        {
            return new Rectangle(panel.X + Ui.S(8), panel.Y + Ui.S(22), panel.Width - Ui.S(16),
                Ui.S(GraphChartHeight));
        }

        /// <summary>面板外框 + 标题 +（可选）通道图例 + 两行说明。</summary>
        private static void PaintPanelChrome(Graphics g, Rectangle rect, string title, string hint, bool legend)
        {
            Palette p = Theme.P;
            PaintKit.FillRound(g, rect, Ui.SF(10f), p.SurfaceSunken);
            PaintKit.StrokeRound(g, rect, Ui.SF(10f), Color.FromArgb(p.Dark ? 60 : 110, p.Border), 1f);

            Rectangle titleRect = new Rectangle(rect.X + Ui.S(8), rect.Y + Ui.S(4), rect.Width - Ui.S(16), Ui.S(16));
            PaintKit.Text(g, title, Fonts.Small, p.Text, titleRect, StringAlignment.Near, StringAlignment.Center,
                false);

            if (legend)
            {
                // 图例放在标题行右侧，避开下面的说明文字
                int lx = rect.Right - Ui.S(8) - Ui.S(38);
                int ly = rect.Y + Ui.S(5);
                PaintKit.Text(g, "R", Fonts.Tiny, Color.FromArgb(235, 90, 90),
                    new Rectangle(lx, ly, Ui.S(10), Ui.S(14)), StringAlignment.Near, StringAlignment.Center, false);
                PaintKit.Text(g, "G", Fonts.Tiny, Color.FromArgb(120, 210, 140),
                    new Rectangle(lx + Ui.S(13), ly, Ui.S(10), Ui.S(14)), StringAlignment.Near,
                    StringAlignment.Center, false);
                PaintKit.Text(g, "B", Fonts.Tiny, Color.FromArgb(130, 170, 250),
                    new Rectangle(lx + Ui.S(26), ly, Ui.S(12), Ui.S(14)), StringAlignment.Near,
                    StringAlignment.Center, false);
            }

            string[] hintLines = (hint ?? string.Empty).Split('\n');
            int hintTop = rect.Y + Ui.S(GraphChartHeight) + Ui.S(26);
            for (int i = 0; i < hintLines.Length; i++)
            {
                PaintKit.Text(g, hintLines[i], Fonts.Tiny, p.TextDim,
                    new Rectangle(rect.X + Ui.S(8), hintTop + i * Ui.S(13), rect.Width - Ui.S(16), Ui.S(13)),
                    StringAlignment.Near, StringAlignment.Center, true);
            }

            // 图底网格
            Rectangle chart = ChartRect(rect);
            for (int i = 1; i < 4; i++)
            {
                int x = chart.X + chart.Width * i / 4;
                g.DrawLine(PaintKit.PenFor(Color.FromArgb(p.Dark ? 30 : 40, p.BorderStrong), 1f), x, chart.Y, x,
                    chart.Bottom);
            }
            for (int i = 1; i < 3; i++)
            {
                int y = chart.Y + chart.Height * i / 3;
                g.DrawLine(PaintKit.PenFor(Color.FromArgb(p.Dark ? 26 : 34, p.BorderStrong), 1f), chart.X, y,
                    chart.Right, y);
            }
        }

        /// <summary>直方图：亮度填充 + 三通道折线。</summary>
        private void PaintHistogramChart(Graphics g, Rectangle chart)
        {
            Palette p = Theme.P;
            float baseline = chart.Bottom;
            float previousX = chart.X;
            float previousY = baseline;
            using (GraphicsPath path = new GraphicsPath())
            {
                for (int i = 0; i < 256; i++)
                {
                    float x = chart.X + chart.Width * (i / 255f);
                    float y = baseline - chart.Height * Math.Min(1f, _histogram.Luma[i] / (float)_histogram.Peak);
                    path.AddLine(previousX, previousY, x, y);
                    previousX = x;
                    previousY = y;
                }
                path.AddLine(previousX, previousY, (float)chart.Right, baseline);
                path.CloseFigure();
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(p.Dark ? 70 : 55, p.Text)))
                {
                    g.FillPath(brush, path);
                }
            }

            PaintChannel(g, chart, _histogram.Red, Color.FromArgb(150, 235, 90, 90));
            PaintChannel(g, chart, _histogram.Green, Color.FromArgb(150, 90, 205, 120));
            PaintChannel(g, chart, _histogram.Blue, Color.FromArgb(150, 95, 150, 245));
        }

        /// <summary>波形图：横轴是画面横向位置，纵轴是亮度，点的亮度代表该处该亮度的像素多少。</summary>
        private void PaintWaveformChart(Graphics g, Rectangle chart)
        {
            Palette p = Theme.P;
            Color ink = p.Dark ? Color.FromArgb(255, 240, 240, 245) : Color.FromArgb(255, 30, 30, 35);
            Bitmap wave = _histogram.RenderWaveBitmap(ink);
            if (wave != null)
            {
                GraphicsState state = g.Save();
                try
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(wave, chart);
                }
                finally
                {
                    g.Restore(state);
                }
            }


        }

        /// <summary>RGB 分量：三条通道曲线各自归一化，便于看偏色。</summary>
        private void PaintChannelsChart(Graphics g, Rectangle chart)
        {
            PaintChannel(g, chart, _histogram.Red, Color.FromArgb(230, 235, 90, 90));
            PaintChannel(g, chart, _histogram.Green, Color.FromArgb(230, 90, 205, 120));
            PaintChannel(g, chart, _histogram.Blue, Color.FromArgb(230, 95, 150, 245));
        }
        private static void PaintChannel(Graphics g, Rectangle chart, int[] bins, Color color)
        {
            int peak = 1;
            for (int i = 0; i < 256; i++)
            {
                if (bins[i] > peak)
                {
                    peak = bins[i];
                }
            }

            Pen pen = PaintKit.PenFor(color, 1f);
            float step = chart.Width / 255f;
            using (GraphicsPath path = new GraphicsPath())
            {
                float previousX = chart.X;
                float previousY = chart.Bottom - chart.Height * Math.Min(1f, bins[0] / (float)peak);
                for (int i = 1; i < 256; i++)
                {
                    float x = chart.X + step * i;
                    float y = chart.Bottom - chart.Height * Math.Min(1f, bins[i] / (float)peak);
                    path.AddLine(previousX, previousY, x, y);
                    previousX = x;
                    previousY = y;
                }
                g.DrawPath(pen, path);
            }
        }

        private void PaintRows(Graphics g)
        {
            Palette p = Theme.P;
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(_rowsRect, CombineMode.Intersect);
                int y = _rowsRect.Y - _scroll;
                for (int i = 0; i < _rows.Count; i++)
                {
                    Row row = _rows[i];
                    if (row.Header)
                    {
                        y += Ui.S(6);
                        PaintKit.Text(g, row.Label, Fonts.Small, p.TextDim,
                            new Rectangle(_rowsRect.X, y, _rowsRect.Width, Ui.S(22)), StringAlignment.Near,
                            StringAlignment.Center, false);
                        y += Ui.S(SectionHeight) - Ui.S(6);
                        continue;
                    }

                    Rectangle labelRect = new Rectangle(_rowsRect.X, y, Ui.S(84), Ui.S(RowHeight));
                    PaintKit.Text(g, row.Label, Fonts.Small, p.TextDim, labelRect, StringAlignment.Near,
                        StringAlignment.Center, false);

                    Rectangle valueRect = new Rectangle(_rowsRect.X + Ui.S(88), y,
                        _rowsRect.Width - Ui.S(88), Ui.S(RowHeight));
                    PaintKit.Text(g, row.Value, Fonts.Body, p.Text, valueRect, StringAlignment.Near,
                        StringAlignment.Center, true);
                    y += Ui.S(RowHeight);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>行区底部的渐隐，暗示下面还有内容、且不是被硬切。</summary>
        private void PaintRowsFade(Graphics g)
        {
            int maxScroll = _contentHeight - _rowsRect.Height;
            if (maxScroll <= 2 || _scroll >= maxScroll - 2)
            {
                return;
            }
            Palette p = Theme.P;
            int height = Ui.S(22);
            Rectangle fade = new Rectangle(_rowsRect.X, _rowsRect.Bottom - height, _rowsRect.Width, height);
            using (System.Drawing.Drawing2D.LinearGradientBrush brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                fade, Color.FromArgb(0, p.Surface), Color.FromArgb(235, p.Surface), 90f))
            {
                g.FillRectangle(brush, fade);
            }
        }

        private void PaintScrollBar(Graphics g)
        {
            Palette p = Theme.P;
            int width = Ui.S(4);
            int x = _rowsRect.Right + Ui.S(6);
            int trackTop = _rowsRect.Y;
            int trackHeight = _rowsRect.Height;
            int max = Math.Max(1, _contentHeight - _rowsRect.Height);
            int thumbHeight = Math.Max(Ui.S(24), (int)(trackHeight * (_rowsRect.Height / (float)_contentHeight)));
            int thumbTop = trackTop + (int)((trackHeight - thumbHeight) * (_scroll / (float)max));
            PaintKit.FillRound(g, new Rectangle(x, trackTop, width, trackHeight), width / 2f,
                Color.FromArgb(p.Dark ? 46 : 40, p.BorderStrong));
            PaintKit.FillRound(g, new Rectangle(x, thumbTop, width, thumbHeight), width / 2f,
                Color.FromArgb(p.Dark ? 190 : 170, p.BorderStrong));
        }

        // ---------------- 输入 ----------------

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            bool hot = _closeRect.Contains(point);
            if (hot != _hotClose)
            {
                _hotClose = hot;
                return true;
            }
            return false;
        }

        internal override bool HandleMouseDown(Point point, ViewerForm form)
        {
            form.SetOverlay(null);
            return true;
        }

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Escape || keyData == Keys.Enter || keyData == Keys.Space)
            {
                form.SetOverlay(null);
                return true;
            }
            if (keyData == Keys.Down || keyData == Keys.PageDown)
            {
                _scroll += Ui.S(RowHeight * 3);
                ClampScroll();
                return true;
            }
            if (keyData == Keys.Up || keyData == Keys.PageUp)
            {
                _scroll -= Ui.S(RowHeight * 3);
                ClampScroll();
                return true;
            }
            return false;
        }
    }
}
