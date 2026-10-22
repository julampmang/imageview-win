using System;
using System.Drawing;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>
    /// 调整浮层：亮度 / 对比度 / 饱和度 / 色温 / 拉直 / 锐化 + 自动色阶。
    ///
    /// 前四项与自动色阶合成一次 ColorMatrix（零额外位图）；拉直只是 GDI+ 矩阵变换（不碰像素）；
    /// 只有锐化要卷积，所以拖动滑杆时先关预览、松手后再算一次（限制 ≤2000 边长，避免多占内存）。
    /// </summary>
    internal sealed class AdjustOverlay : Overlay
    {
        private sealed class Slider
        {
            internal Rectangle Row;
            internal Rectangle Track;
        }

        private static readonly string[] Labels = new string[] { "亮度", "对比度", "饱和度", "色温", "拉直", "锐化" };
        private static readonly float[] MinValues = new float[] { -100f, -100f, -100f, -100f, -45f, 0f };
        private static readonly float[] MaxValues = new float[] { 100f, 100f, 100f, 100f, 45f, 100f };

        private readonly Slider[] _sliders = new Slider[6];
        private EditRecipe _original;
        private Histogram _histogram;

        private Rectangle _card;
        private Rectangle _autoRect;
        private Rectangle _resetRect;
        private Rectangle _cancelRect;
        private Rectangle _applyRect;

        private int _dragging = -1;
        private int _hot = -1;
        private bool _busy;

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            if (_original == null)
            {
                _original = form.Recipe.Clone();
            }

            int width = Ui.S(620);
            int height = Ui.S(452);
            _card = OverlayPaint.CenterCard(client, width, Math.Min(height, client.Height - Ui.S(30)));

            int pad = Ui.S(24);
            int top = _card.Y + Ui.S(74);
            int rowHeight = Ui.S(44);
            for (int i = 0; i < _sliders.Length; i++)
            {
                if (_sliders[i] == null)
                {
                    _sliders[i] = new Slider();
                }
                Slider slider = _sliders[i];
                slider.Row = new Rectangle(_card.X + pad, top + i * rowHeight, _card.Width - pad * 2, rowHeight);
                int trackX = slider.Row.X + Ui.S(74);
                int trackWidth = slider.Row.Width - Ui.S(74) - Ui.S(64);
                slider.Track = new Rectangle(trackX, slider.Row.Y + slider.Row.Height / 2 - Ui.S(3), trackWidth,
                    Ui.S(6));
            }

            int buttonY = _card.Bottom - pad - Ui.S(34);
            int buttonHeight = Ui.S(34);
            _applyRect = new Rectangle(_card.Right - pad - Ui.S(120), buttonY, Ui.S(120), buttonHeight);
            _cancelRect = new Rectangle(_applyRect.X - Ui.S(10) - Ui.S(76), buttonY, Ui.S(76), buttonHeight);
            _resetRect = new Rectangle(_cancelRect.X - Ui.S(10) - Ui.S(76), buttonY, Ui.S(76), buttonHeight);
            _autoRect = new Rectangle(_card.X + pad, buttonY, Ui.S(104), buttonHeight);
        }

        // ---------------- 绘制 ----------------

        internal override void Paint(Graphics g, ViewerForm form)
        {
            Palette p = Theme.P;
            OverlayPaint.Scrim(g, form);
            OverlayPaint.Card(g, _card, "调整",
                "实时预览 · 拉直会自动裁掉四角 · 应用时另存副本，不改动原图");

            EditRecipe recipe = form.Recipe;
            for (int i = 0; i < _sliders.Length; i++)
            {
                PaintSlider(g, _sliders[i], i, Value(recipe, i));
            }

            OverlayPaint.Button(g, _autoRect, "自动色阶", false, _hot == 1, false);
            OverlayPaint.Button(g, _resetRect, "重置", false, _hot == 2, false);
            OverlayPaint.Button(g, _cancelRect, Strings.EditCancel, false, _hot == 3, false);
            OverlayPaint.Button(g, _applyRect, _busy ? "正在保存…" : "应用并另存", true, _hot == 4, false);

            // 说明文字只能用「自动色阶」与「取消」之间的这点空档，宽度按实际间距算，
            // 写死宽度就会压到取消按钮上（之前就是这么重叠的）。
            int hintX = _autoRect.Right + Ui.S(10);
            int hintWidth = _cancelRect.X - Ui.S(12) - hintX;
            if (hintWidth > Ui.S(40))
            {
                PaintKit.Text(g, "按每通道 0.6% / 99.4% 分位拉伸黑白点", Fonts.Tiny, p.TextDim,
                    new Rectangle(hintX, _autoRect.Y, hintWidth, _autoRect.Height), StringAlignment.Near,
                    StringAlignment.Center, true);
            }
        }

        private static float Value(EditRecipe recipe, int index)
        {
            switch (index)
            {
                case 0: return recipe.Brightness;
                case 1: return recipe.Contrast;
                case 2: return recipe.Saturation;
                case 3: return recipe.Warmth;
                case 4: return recipe.Angle;
                default: return recipe.Sharpen;
            }
        }

        private static void SetValue(EditRecipe recipe, int index, float value)
        {
            switch (index)
            {
                case 0: recipe.Brightness = value; break;
                case 1: recipe.Contrast = value; break;
                case 2: recipe.Saturation = value; break;
                case 3: recipe.Warmth = value; break;
                case 4: recipe.Angle = value; break;
                default: recipe.Sharpen = value; break;
            }
        }

        private static string FormatValue(int index, float value)
        {
            if (index == 4)
            {
                return value.ToString("0.0") + "°";
            }
            string text = Math.Round(value).ToString();
            return value > 0.5f ? "+" + text : text;
        }

        private void PaintSlider(Graphics g, Slider slider, int index, float value)
        {
            Palette p = Theme.P;
            PaintKit.Text(g, Labels[index], Fonts.Body, p.Text,
                new Rectangle(slider.Row.X, slider.Row.Y, Ui.S(70), slider.Row.Height), StringAlignment.Near,
                StringAlignment.Center, false);

            Rectangle track = slider.Track;
            PaintKit.FillRound(g, track, track.Height / 2f, Color.FromArgb(p.Dark ? 60 : 70, p.BorderStrong));

            float span = MaxValues[index] - MinValues[index];
            float ratio = span <= 0f ? 0f : (value - MinValues[index]) / span;
            if (ratio < 0f) { ratio = 0f; }
            if (ratio > 1f) { ratio = 1f; }
            int filled = (int)(track.Width * ratio);
            if (filled > 0)
            {
                PaintKit.FillRound(g, new Rectangle(track.X, track.Y, filled, track.Height), track.Height / 2f,
                    p.Accent);
            }

            if (MinValues[index] < 0f && MaxValues[index] > 0f)
            {
                float zeroRatio = (0f - MinValues[index]) / span;
                int zeroX = track.X + (int)(track.Width * zeroRatio);
                PaintKit.FillRound(g, new Rectangle(zeroX, track.Y - Ui.S(4), 1, track.Height + Ui.S(8)), 0.5f,
                    Color.FromArgb(90, p.TextDim));
            }

            int thumbX = track.X + filled;
            int cy = track.Y + track.Height / 2;
            using (SolidBrush thumb = new SolidBrush(Color.FromArgb(255, 250, 250, 252)))
            using (Pen edge = new Pen(p.Accent, 1.5f))
            {
                g.FillEllipse(thumb, thumbX - Ui.S(7), cy - Ui.S(7), Ui.S(14), Ui.S(14));
                g.DrawEllipse(edge, thumbX - Ui.S(7), cy - Ui.S(7), Ui.S(14), Ui.S(14));
            }

            PaintKit.Text(g, FormatValue(index, value), Fonts.BodyStrong, p.Text,
                new Rectangle(track.Right + Ui.S(10), slider.Row.Y, Ui.S(52), slider.Row.Height),
                StringAlignment.Far, StringAlignment.Center, false);
        }

        // ---------------- 输入 ----------------

        private int HitButton(Point point)
        {
            if (_autoRect.Contains(point)) { return 1; }
            if (_resetRect.Contains(point)) { return 2; }
            if (_cancelRect.Contains(point)) { return 3; }
            if (_applyRect.Contains(point)) { return 4; }
            return -1;
        }

        private int HitSlider(Point point)
        {
            for (int i = 0; i < _sliders.Length; i++)
            {
                Rectangle row = _sliders[i].Row;
                if (point.Y >= row.Y && point.Y <= row.Bottom &&
                    point.X >= _sliders[i].Track.X - Ui.S(10) && point.X <= _sliders[i].Track.Right + Ui.S(10))
                {
                    return i;
                }
            }
            return -1;
        }

        private void UpdateFromX(ViewerForm form, int index, int x)
        {
            Slider slider = _sliders[index];
            float ratio = (x - slider.Track.X) / (float)Math.Max(1, slider.Track.Width);
            if (ratio < 0f) { ratio = 0f; }
            if (ratio > 1f) { ratio = 1f; }
            float span = MaxValues[index] - MinValues[index];
            float value = MinValues[index] + ratio * span;
            value = index == 4 ? (float)Math.Round(value, 1) : (float)Math.Round(value);
            SetValue(form.Recipe, index, value);

            // 拉直会改变有效尺寸，需要重新适配；其余只是重画
            form.OnRecipeChanged(index == 4);
        }

        internal override bool HandleMouseDown(Point point, ViewerForm form)
        {
            int button = HitButton(point);
            if (button == 1)
            {
                if (_histogram == null && form.Document != null)
                {
                    _histogram = Histogram.Compute(form.Document.Preview);
                }
                if (form.Recipe.TryAutoLevels(_histogram))
                {
                    form.OnRecipeChanged(false);
                }
                else
                {
                    form.ShowToast("没有可用的直方图，无法自动色阶");
                }
                return true;
            }
            if (button == 2)
            {
                form.Recipe.ResetColor();
                form.Recipe.Angle = 0f;
                form.Recipe.Sharpen = 0f;
                form.Recipe.ReleaseSharpened();
                form.OnRecipeChanged(true);
                return true;
            }
            if (button == 3)
            {
                Cancel(form);
                return true;
            }
            if (button == 4)
            {
                if (!_busy)
                {
                    _busy = true;
                    form.Invalidate();
                    form.SaveEditedCopy();
                    _busy = false;
                    form.Invalidate();
                }
                return true;
            }

            int slider = HitSlider(point);
            if (slider >= 0)
            {
                _dragging = slider;
                if (slider == 5)
                {
                    // 锐化每帧都要卷积，拖动期间先关掉预览
                    form.Recipe.SuppressSharpenPreview = true;
                    form.Recipe.ReleaseSharpened();
                }
                UpdateFromX(form, slider, point.X);
                return true;
            }

            if (!_card.Contains(point))
            {
                Cancel(form);
            }
            return true;
        }

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            if (_dragging >= 0)
            {
                UpdateFromX(form, _dragging, point.X);
                return true;
            }
            int hot = HitButton(point);
            if (hot < 0 && HitSlider(point) >= 0)
            {
                hot = 0;
            }
            if (hot != _hot)
            {
                _hot = hot;
                form.Invalidate();
            }
            form.Cursor = HitSlider(point) >= 0 ? Cursors.Hand : Cursors.Default;
            return false;
        }

        internal override bool HandleMouseUp(Point point, ViewerForm form)
        {
            if (_dragging >= 0)
            {
                if (_dragging == 5)
                {
                    form.Recipe.SuppressSharpenPreview = false;
                    form.Recipe.ReleaseSharpened();
                    form.OnRecipeChanged(false);
                }
                _dragging = -1;
                return true;
            }
            return false;
        }

        private void Cancel(ViewerForm form)
        {
            if (_original != null)
            {
                form.SetRecipe(_original, true);
            }
            form.SetOverlay(null);
        }

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Escape)
            {
                Cancel(form);
                return true;
            }
            if (keyData == Keys.Enter)
            {
                form.SaveEditedCopy();
                return true;
            }
            return false;
        }

        internal override void Detach(ViewerForm form)
        {
            form.Cursor = Cursors.Default;
            form.Recipe.SuppressSharpenPreview = false;
        }
    }
}
