using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>
    /// 调整尺寸浮层：宽/高输入（原生 TextBox 伪装成无边框深色输入框）、
    /// 锁定比例、百分比快选、输出格式、质量、目标路径、覆盖原文件。
    /// </summary>
    internal sealed class ResizeOverlay : Overlay
    {
        private readonly ImageDocument _document;
        private readonly TextBox _widthBox;
        private readonly TextBox _heightBox;

        private readonly List<GdiEncoder.Format> _formats;
        private GdiEncoder.Format _format;
        private bool _keepOriginalFormat = true;
        private int _quality = 90;
        private bool _lockRatio = true;
        private bool _syncing;
        private bool _overwrite;
        private bool _confirmingOverwrite;
        private int _hot = -1;
        private int _draggingSlider = -1;
        private bool _busy;
        private string _targetPath;
        private string _status;

        private Rectangle _card;
        private Rectangle _lockRect;
        private readonly Rectangle[] _percentRects = new Rectangle[4];
        private readonly Rectangle[] _formatRects = new Rectangle[6];
        private Rectangle _qualityTrack;
        private Rectangle _browseRect;
        private Rectangle _overwriteRect;
        private Rectangle _cancelRect;
        private Rectangle _saveRect;

        private static readonly int[] Percents = new int[] { 25, 50, 75, 100 };

        internal ResizeOverlay(ImageDocument document)
        {
            _document = document;
            _formats = GdiEncoder.Formats();
            _format = _formats.Count > 0 ? _formats[0] : null;

            _widthBox = CreateBox();
            _heightBox = CreateBox();
            if (document != null && document.IsValid)
            {
                _widthBox.Text = document.PixelWidth.ToString();
                _heightBox.Text = document.PixelHeight.ToString();
                _targetPath = ResizeJob.SuggestPath(document.FilePath, document.PixelWidth, document.PixelHeight,
                    PreferredExtension());
            }
            _widthBox.TextChanged += delegate { OnSizeChanged(_widthBox, true); };
            _heightBox.TextChanged += delegate { OnSizeChanged(_heightBox, false); };
            _widthBox.KeyPress += OnBoxKeyPress;
            _heightBox.KeyPress += OnBoxKeyPress;
        }

        private static TextBox CreateBox()
        {
            TextBox box = new TextBox();
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.P.SurfaceSunken;
            box.ForeColor = Theme.P.Text;
            box.Font = Fonts.Body;
            box.TextAlign = HorizontalAlignment.Center;
            box.MaxLength = 5;
            return box;
        }

        private string PreferredExtension()
        {
            if (_document == null)
            {
                return ".png";
            }
            string extension = Path.GetExtension(_document.FilePath);
            if (GdiEncoder.FindByExtension(extension) != null)
            {
                return extension.ToLowerInvariant();
            }
            return ".png";
        }

        // ---------------- 尺寸联动 ----------------

        private void OnBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void OnSizeChanged(TextBox box, bool widthChanged)
        {
            if (_syncing || _document == null || !_document.IsValid)
            {
                return;
            }
            int value;
            if (!int.TryParse(box.Text, out value) || !ResizeJob.IsValidSide(value))
            {
                return;
            }
            _syncing = true;
            try
            {
                if (_lockRatio)
                {
                    if (widthChanged)
                    {
                        _heightBox.Text = ResizeJob.ScaleOtherSide(value, _document.PixelWidth,
                            _document.PixelHeight).ToString();
                    }
                    else
                    {
                        _widthBox.Text = ResizeJob.ScaleOtherSide(value, _document.PixelHeight,
                            _document.PixelWidth).ToString();
                    }
                }
                UpdateTargetPath();
            }
            finally
            {
                _syncing = false;
            }
        }

        private void ApplyPercent(int percent)
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            _syncing = true;
            try
            {
                _widthBox.Text = ResizeJob.SideForPercent(_document.PixelWidth, percent).ToString();
                _heightBox.Text = ResizeJob.SideForPercent(_document.PixelHeight, percent).ToString();
                UpdateTargetPath();
            }
            finally
            {
                _syncing = false;
            }
            _confirmingOverwrite = false;
        }

        private void UpdateTargetPath()
        {
            int w, h;
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            if (!int.TryParse(_widthBox.Text, out w) || !int.TryParse(_heightBox.Text, out h))
            {
                return;
            }
            if (_overwrite)
            {
                _targetPath = _document.FilePath;
                return;
            }
            string extension = _keepOriginalFormat ? PreferredExtension() : (_format == null ? ".png" : _format.Extension);
            _targetPath = ResizeJob.SuggestPath(_document.FilePath, w, h, extension);
        }

        private GdiEncoder.Format EffectiveFormat()
        {
            if (_keepOriginalFormat)
            {
                return GdiEncoder.FindByExtension(PreferredExtension()) ?? _format;
            }
            return _format;
        }

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            int width = Ui.S(560);
            int height = Ui.S(440);
            _card = OverlayPaint.CenterCard(client, width, height);

            int pad = Ui.S(24);
            int left = _card.X + pad;
            int right = _card.Right - pad;

            // 尺寸输入行
            int rowY = _card.Y + Ui.S(86);
            int boxW = Ui.S(96);
            int boxH = Ui.S(30);
            SetBoxBounds(_widthBox, new Rectangle(left, rowY, boxW, boxH));
            SetBoxBounds(_heightBox, new Rectangle(left + boxW + Ui.S(52), rowY, boxW, boxH));
            _lockRect = new Rectangle(left + boxW + Ui.S(12), rowY + (boxH - Ui.S(20)) / 2, Ui.S(20), Ui.S(20));

            // 百分比
            int percentY = rowY + boxH + Ui.S(16);
            int chipW = Ui.S(64);
            int chipH = Ui.S(28);
            for (int i = 0; i < Percents.Length; i++)
            {
                _percentRects[i] = new Rectangle(left + i * (chipW + Ui.S(8)), percentY, chipW, chipH);
            }

            // 格式
            int formatY = percentY + chipH + Ui.S(26);
            int formatW = Ui.S(74);
            int formatH = Ui.S(30);
            _formatRects[0] = new Rectangle(left, formatY, Ui.S(98), formatH);
            for (int i = 0; i < _formats.Count && i < 5; i++)
            {
                _formatRects[i + 1] = new Rectangle(left + Ui.S(106) + i * (formatW + Ui.S(6)), formatY, formatW,
                    formatH);
            }

            // 质量滑杆
            int qualityY = formatY + formatH + Ui.S(26);
            _qualityTrack = new Rectangle(left, qualityY + Ui.S(8), right - left - Ui.S(52), Ui.S(6));

            // 目标路径
            int pathY = qualityY + Ui.S(40);
            _browseRect = new Rectangle(right - Ui.S(84), pathY - Ui.S(6), Ui.S(84), Ui.S(30));
            _overwriteRect = new Rectangle(left, pathY + Ui.S(30), Ui.S(20), Ui.S(20));

            // 底部按钮
            int buttonH = Ui.S(34);
            int buttonW = Ui.S(96);
            _saveRect = new Rectangle(right - buttonW, _card.Bottom - pad - buttonH, buttonW, buttonH);
            _cancelRect = new Rectangle(_saveRect.X - Ui.S(10) - buttonW, _saveRect.Y, buttonW, buttonH);

            _widthBox.BackColor = Theme.P.SurfaceSunken;
            _widthBox.ForeColor = Theme.P.Text;
            _heightBox.BackColor = Theme.P.SurfaceSunken;
            _heightBox.ForeColor = Theme.P.Text;
            if (!form.Controls.Contains(_widthBox))
            {
                form.Controls.Add(_widthBox);
                form.Controls.Add(_heightBox);
            }
            _widthBox.BringToFront();
            _heightBox.BringToFront();
        }

        private static void SetBoxBounds(TextBox box, Rectangle rect)
        {
            box.Bounds = new Rectangle(rect.X, rect.Y + (rect.Height - box.PreferredHeight) / 2, rect.Width,
                box.PreferredHeight);
        }

        internal override void Detach(ViewerForm form)
        {
            if (form.Controls.Contains(_widthBox))
            {
                form.Controls.Remove(_widthBox);
            }
            if (form.Controls.Contains(_heightBox))
            {
                form.Controls.Remove(_heightBox);
            }
            _widthBox.Dispose();
            _heightBox.Dispose();
        }

        // ---------------- 绘制 ----------------

        internal override void Paint(Graphics g, ViewerForm form)
        {
            Palette p = Theme.P;
            OverlayPaint.Scrim(g, form);
            if (_card.IsEmpty)
            {
                Layout(form, form.ClientRectangle);
            }

            string subtitle = _document != null && _document.IsValid
                ? Strings.ResizeCurrent + " " + _document.PixelWidth + " × " + _document.PixelHeight
                : null;
            OverlayPaint.Card(g, _card, Strings.ResizeTitle, subtitle);

            int pad = Ui.S(24);
            int left = _card.X + pad;

            // 标签：宽度 / 高度
            int labelY = _widthBox.Top - Ui.S(22);
            PaintKit.Text(g, Strings.ResizeWidth, Fonts.Small, p.TextDim,
                new Rectangle(left, labelY, Ui.S(80), Ui.S(20)), StringAlignment.Near, StringAlignment.Center, false);
            PaintKit.Text(g, Strings.ResizeHeight, Fonts.Small, p.TextDim,
                new Rectangle(_heightBox.Left, labelY, Ui.S(80), Ui.S(20)), StringAlignment.Near,
                StringAlignment.Center, false);

            // 输入框底
            PaintKit.FillRound(g, new Rectangle(_widthBox.Left, _widthBox.Top - Ui.S(4), _widthBox.Width,
                _widthBox.Height + Ui.S(8)), Ui.SF(6f), p.SurfaceSunken);
            PaintKit.FillRound(g, new Rectangle(_heightBox.Left, _heightBox.Top - Ui.S(4), _heightBox.Width,
                _heightBox.Height + Ui.S(8)), Ui.SF(6f), p.SurfaceSunken);

            // 乘号
            PaintKit.Text(g, "×", Fonts.Body, p.TextDim,
                new Rectangle(_widthBox.Right + Ui.S(8), _widthBox.Top - Ui.S(4), Ui.S(32), _widthBox.Height),
                StringAlignment.Center, StringAlignment.Center, false);

            // 锁定比例
            PaintLock(g, p);

            // 百分比
            for (int i = 0; i < Percents.Length; i++)
            {
                bool hot = _hot == 100 + i;
                PaintChip(g, _percentRects[i], Percents[i] + "%", false, hot);
            }

            // 格式
            bool keepFormatHot = _hot == 200;
            PaintChip(g, _formatRects[0], Strings.ResizeKeepFormat, _keepOriginalFormat, keepFormatHot);
            for (int i = 0; i < _formats.Count && i < 5; i++)
            {
                bool selected = !_keepOriginalFormat && _format != null &&
                                string.Equals(_format.Name, _formats[i].Name, StringComparison.Ordinal);
                PaintChip(g, _formatRects[i + 1], _formats[i].DisplayName, selected, _hot == 201 + i);
            }

            // 质量
            GdiEncoder.Format effective = EffectiveFormat();
            bool qualityEnabled = effective != null && effective.SupportsQuality;
            Color trackColor = qualityEnabled ? p.BorderStrong : Color.FromArgb(80, p.Border);
            PaintKit.FillRound(g, _qualityTrack, Ui.SF(3f), trackColor);
            if (qualityEnabled)
            {
                int knobX = _qualityTrack.X + (int)(_qualityTrack.Width * (_quality / 100.0));
                Rectangle filled = new Rectangle(_qualityTrack.X, _qualityTrack.Y,
                    Math.Max(Ui.S(3), knobX - _qualityTrack.X), _qualityTrack.Height);
                PaintKit.FillRound(g, filled, Ui.SF(3f), p.Accent);
                PaintKit.FillRound(g, new Rectangle(knobX - Ui.S(6), _qualityTrack.Y - Ui.S(5), Ui.S(12), Ui.S(16)),
                    Ui.SF(6f), p.Text);
            }
            string qualityText = qualityEnabled ? Strings.ResizeQuality + " " + _quality : Strings.ResizeQuality + " —";
            PaintKit.Text(g, qualityText, Fonts.Small, qualityEnabled ? p.TextDim : Color.FromArgb(110, p.TextDim),
                new Rectangle(_qualityTrack.Right + Ui.S(8), _qualityTrack.Y - Ui.S(8), Ui.S(60), Ui.S(24)),
                StringAlignment.Near, StringAlignment.Center, false);

            // 目标路径
            PaintKit.Text(g, Strings.ResizeTarget, Fonts.Small, p.TextDim,
                new Rectangle(left, _browseRect.Y - Ui.S(24), Ui.S(120), Ui.S(20)), StringAlignment.Near,
                StringAlignment.Center, false);
            Rectangle pathRect = new Rectangle(left, _browseRect.Y, _browseRect.X - left - Ui.S(8),
                _browseRect.Height);
            PaintKit.FillRound(g, pathRect, Ui.SF(6f), p.SurfaceSunken);
            string display = _targetPath ?? string.Empty;
            PaintKit.Text(g, display, Fonts.Small, p.Text, new Rectangle(pathRect.X + Ui.S(8), pathRect.Y,
                pathRect.Width - Ui.S(16), pathRect.Height), StringAlignment.Near, StringAlignment.Center, true);
            OverlayPaint.Button(g, _browseRect, Strings.ResizeBrowse, false, _hot == 300, false);

            // 覆盖原文件
            PaintCheck(g, p, _overwrite);

            // 说明
            string note = _overwrite
                ? (_confirmingOverwrite ? "再次点击「确认覆盖」会直接替换原文件" : "覆盖会替换原文件，点击保存后需再确认一次")
                : Strings.ResizeRotationNote;
            PaintKit.Text(g, note, Fonts.Small, _overwrite ? p.Danger : p.TextDim,
                new Rectangle(left, _overwriteRect.Bottom + Ui.S(6), _card.Width - pad * 2, Ui.S(40)),
                StringAlignment.Near, StringAlignment.Near, true);

            // 状态（保存失败原因等）
            if (!string.IsNullOrEmpty(_status))
            {
                PaintKit.Text(g, _status, Fonts.Small, p.Danger,
                    new Rectangle(left, _saveRect.Y - Ui.S(24), _card.Width - pad * 2, Ui.S(20)),
                    StringAlignment.Near, StringAlignment.Center, true);
            }

            // 按钮
            OverlayPaint.Button(g, _cancelRect, Strings.ResizeCancel, false, _hot == 400, false);
            string saveText = _busy
                ? Strings.ResizeSaving
                : (_confirmingOverwrite ? "确认覆盖" : Strings.ResizeApply);
            OverlayPaint.Button(g, _saveRect, saveText, true, _hot == 401, _confirmingOverwrite);
        }

        private void PaintLock(Graphics g, Palette p)
        {
            bool hot = _hot == 500;
            Color fill = _lockRatio ? p.Accent : (hot ? p.SurfaceAlt : p.SurfaceSunken);
            PaintKit.FillRound(g, _lockRect, Ui.SF(5f), fill);
            PaintKit.StrokeRound(g, _lockRect, Ui.SF(5f), Color.FromArgb(140, p.BorderStrong), 1f);
            if (_lockRatio)
            {
                PaintKit.Icon(g, IconId.Check, new Rectangle(_lockRect.X + Ui.S(3), _lockRect.Y + Ui.S(3),
                    _lockRect.Width - Ui.S(6), _lockRect.Height - Ui.S(6)), Color.White, 1.8f);
            }
        }

        private void PaintCheck(Graphics g, Palette p, bool value)
        {
            Color fill = value ? p.Danger : (Color.FromArgb(0, p.Surface));
            PaintKit.FillRound(g, _overwriteRect, Ui.SF(4f), value ? fill : p.SurfaceSunken);
            PaintKit.StrokeRound(g, _overwriteRect, Ui.SF(4f), Color.FromArgb(150, p.BorderStrong), 1f);
            if (value)
            {
                PaintKit.Icon(g, IconId.Check, new Rectangle(_overwriteRect.X + Ui.S(2), _overwriteRect.Y + Ui.S(2),
                    _overwriteRect.Width - Ui.S(4), _overwriteRect.Height - Ui.S(4)), Color.White, 1.8f);
            }
            PaintKit.Text(g, Strings.ResizeOverwrite, Fonts.Body, p.Text,
                new Rectangle(_overwriteRect.Right + Ui.S(10), _overwriteRect.Y - Ui.S(2), Ui.S(160), Ui.S(24)),
                StringAlignment.Near, StringAlignment.Center, false);
        }

        private void PaintChip(Graphics g, Rectangle rect, string text, bool selected, bool hot)
        {
            Palette p = Theme.P;
            Color fill;
            Color ink;
            if (selected)
            {
                fill = p.AccentSoft;
                ink = p.Accent;
            }
            else if (hot)
            {
                fill = p.SurfaceAlt;
                ink = p.Text;
            }
            else
            {
                fill = p.SurfaceSunken;
                ink = p.TextDim;
            }
            PaintKit.FillRound(g, rect, Ui.SF(7f), fill);
            if (selected || hot)
            {
                PaintKit.StrokeRound(g, rect, Ui.SF(7f), Color.FromArgb(selected ? 160 : 90, p.Accent), 1f);
            }
            PaintKit.Text(g, text, Fonts.Small, ink, rect, StringAlignment.Center, StringAlignment.Center, false);
        }

        // ---------------- 输入 ----------------

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            if (_draggingSlider == 0)
            {
                SetQualityFromPoint(point.X);
                return true;
            }
            int hot = HitTestHot(point);
            if (hot != _hot)
            {
                _hot = hot;
                return true;
            }
            return false;
        }

        private int HitTestHot(Point point)
        {
            for (int i = 0; i < Percents.Length; i++)
            {
                if (_percentRects[i].Contains(point))
                {
                    return 100 + i;
                }
            }
            if (_formatRects[0].Contains(point))
            {
                return 200;
            }
            for (int i = 0; i < _formats.Count && i < 5; i++)
            {
                if (_formatRects[i + 1].Contains(point))
                {
                    return 201 + i;
                }
            }
            if (_browseRect.Contains(point))
            {
                return 300;
            }
            if (_cancelRect.Contains(point))
            {
                return 400;
            }
            if (_saveRect.Contains(point))
            {
                return 401;
            }
            if (_lockRect.Contains(point))
            {
                return 500;
            }
            return -1;
        }

        internal override bool HandleMouseDown(Point point, ViewerForm form)
        {
            if (!_card.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }

            if (_lockRect.Contains(point))
            {
                _lockRatio = !_lockRatio;
                if (_lockRatio)
                {
                    OnSizeChanged(_widthBox, true);
                }
                return true;
            }
            for (int i = 0; i < Percents.Length; i++)
            {
                if (_percentRects[i].Contains(point))
                {
                    ApplyPercent(Percents[i]);
                    return true;
                }
            }
            if (_formatRects[0].Contains(point))
            {
                _keepOriginalFormat = true;
                _confirmingOverwrite = false;
                UpdateTargetPath();
                return true;
            }
            for (int i = 0; i < _formats.Count && i < 5; i++)
            {
                if (_formatRects[i + 1].Contains(point))
                {
                    _keepOriginalFormat = false;
                    _format = _formats[i];
                    _confirmingOverwrite = false;
                    UpdateTargetPath();
                    return true;
                }
            }

            GdiEncoder.Format effective = EffectiveFormat();
            if (effective != null && effective.SupportsQuality)
            {
                Rectangle trackHit = new Rectangle(_qualityTrack.X - Ui.S(6), _qualityTrack.Y - Ui.S(10),
                    _qualityTrack.Width + Ui.S(12), Ui.S(26));
                if (trackHit.Contains(point))
                {
                    _draggingSlider = 0;
                    SetQualityFromPoint(point.X);
                    return true;
                }
            }

            if (_browseRect.Contains(point))
            {
                Browse(form);
                return true;
            }
            if (_overwriteRect.Contains(point) ||
                new Rectangle(_overwriteRect.Right + Ui.S(10), _overwriteRect.Y - Ui.S(2), Ui.S(160), Ui.S(24))
                    .Contains(point))
            {
                _overwrite = !_overwrite;
                _confirmingOverwrite = false;
                UpdateTargetPath();
                return true;
            }
            if (_cancelRect.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }
            if (_saveRect.Contains(point))
            {
                Commit(form);
                return true;
            }
            return true;
        }

        internal override bool HandleMouseUp(Point point, ViewerForm form)
        {
            if (_draggingSlider == 0)
            {
                _draggingSlider = -1;
                return true;
            }
            return false;
        }

        private void SetQualityFromPoint(int x)
        {
            if (_qualityTrack.Width <= 0)
            {
                return;
            }
            double ratio = (x - _qualityTrack.X) / (double)_qualityTrack.Width;
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            _quality = (int)Math.Round(ratio * 99) + 1;
        }

        private void Browse(ViewerForm form)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = Strings.ResizeTarget;
                dialog.OverwritePrompt = true;
                dialog.Filter = BuildFilter();
                GdiEncoder.Format effective = EffectiveFormat();
                if (effective != null)
                {
                    dialog.DefaultExt = effective.Extension;
                }
                if (!string.IsNullOrEmpty(_targetPath))
                {
                    dialog.FileName = Path.GetFileName(_targetPath);
                    try
                    {
                        dialog.InitialDirectory = Path.GetDirectoryName(_targetPath);
                    }
                    catch (Exception)
                    {
                    }
                }
                if (dialog.ShowDialog(form) == DialogResult.OK)
                {
                    _targetPath = dialog.FileName;
                    _overwrite = false;
                    GdiEncoder.Format chosen = GdiEncoder.FindByExtension(Path.GetExtension(_targetPath));
                    if (chosen != null)
                    {
                        _keepOriginalFormat = false;
                        _format = chosen;
                    }
                }
            }
        }

        private string BuildFilter()
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < _formats.Count; i++)
            {
                parts.Add(_formats[i].DisplayName + " (*" + _formats[i].Extension + ")|*" + _formats[i].Extension);
            }
            parts.Add("所有文件 (*.*)|*.*");
            return string.Join("|", parts.ToArray());
        }

        private void Commit(ViewerForm form)
        {
            if (_busy)
            {
                return;
            }
            int width, height;
            if (!int.TryParse(_widthBox.Text, out width) || !ResizeJob.IsValidSide(width) ||
                !int.TryParse(_heightBox.Text, out height) || !ResizeJob.IsValidSide(height))
            {
                _status = Strings.ResizeRangeHint;
                return;
            }
            GdiEncoder.Format format = EffectiveFormat();
            if (format == null)
            {
                _status = "没有可用的输出编码器";
                return;
            }
            if (string.IsNullOrEmpty(_targetPath))
            {
                _status = "请先选择保存位置";
                return;
            }

            bool overwrite = _overwrite && _document != null &&
                             string.Equals(_targetPath, _document.FilePath, StringComparison.OrdinalIgnoreCase);
            if (overwrite && !_confirmingOverwrite)
            {
                _confirmingOverwrite = true;
                _status = null;
                return;
            }

            _busy = true;
            _status = null;
            form.Invalidate();
            Application.DoEvents();

            ResizeJob.Result result = ResizeJob.Execute(_document, _targetPath, width, height, format, _quality);
            _busy = false;

            if (!result.Ok)
            {
                _status = Strings.ResizeFailed + "：" + result.Error;
                return;
            }

            string target = result.Path;
            form.SetOverlay(null);
            if (overwrite)
            {
                form.ReopenAfterResize(target);
            }
            form.ShowToast(string.Format("{0} {1} × {2} · {3}", Strings.ResizeDone, result.Width, result.Height,
                FormatSize(result.Bytes)));
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

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Escape)
            {
                form.SetOverlay(null);
                return true;
            }
            if (keyData == Keys.Enter)
            {
                Commit(form);
                return true;
            }
            return false;
        }

        internal override bool BlocksInput
        {
            get { return false; }
        }
    }
}
