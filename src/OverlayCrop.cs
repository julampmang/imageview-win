using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>
    /// 裁剪浮层：在图片上拖一个矩形，Enter 应用。裁剪只是记住一个归一化矩形，
    /// 不复制任何像素；预览由画布换源矩形完成，所以拖拽期间零额外开销。
    /// </summary>
    internal sealed class CropOverlay : Overlay
    {
        private const int HandleNone = -1;
        private const int HandleMove = 0;
        private const int HandleTopLeft = 1;
        private const int HandleTopRight = 2;
        private const int HandleBottomRight = 3;
        private const int HandleBottomLeft = 4;
        private const int HandleTop = 5;
        private const int HandleRight = 6;
        private const int HandleBottom = 7;
        private const int HandleLeft = 8;

        private RectangleF _display;
        private RectangleF _rect;
        private RectangleF _startRect;
        private Point _dragStart;
        private int _dragHandle = HandleNone;

        private Rectangle _bar;
        private readonly Rectangle[] _presetRects = new Rectangle[5];
        private Rectangle _applyRect;
        private Rectangle _cancelRect;
        private int _aspectIndex;
        private int _hotButton = -1;

        private static readonly float[][] Aspects = new float[][]
        {
            new float[] { 0f, 0f },      // 自由
            new float[] { 1f, 1f },
            new float[] { 4f, 3f },
            new float[] { 16f, 9f },
            new float[] { 3f, 2f }
        };

        private static readonly string[] AspectLabels = new string[] { "自由", "1:1", "4:3", "16:9", "3:2" };

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            _display = form.GetCanvasDisplayRect();
            if (_rect.IsEmpty || _rect.Width < 8f)
            {
                _rect = _display;
            }
            ClampRect();

            int barWidth = Math.Min(client.Width - Ui.S(40), Ui.S(760));
            int barHeight = Ui.S(58);
            _bar = new Rectangle((client.Width - barWidth) / 2, client.Bottom - barHeight - Ui.S(16), barWidth,
                barHeight);

            int pad = Ui.S(14);
            int buttonHeight = Ui.S(32);
            int y = _bar.Y + (_bar.Height - buttonHeight) / 2;

            int cursor = _bar.X + pad;
            for (int i = 0; i < _presetRects.Length; i++)
            {
                int width = Ui.S(i == 0 ? 52 : 52);
                _presetRects[i] = new Rectangle(cursor, y, width, buttonHeight);
                cursor += width + Ui.S(6);
            }

            _applyRect = new Rectangle(_bar.Right - pad - Ui.S(92), y, Ui.S(92), buttonHeight);
            _cancelRect = new Rectangle(_applyRect.X - Ui.S(10) - Ui.S(76), y, Ui.S(76), buttonHeight);
        }

        private void ClampRect()
        {
            float x = Math.Max(_display.X, Math.Min(_rect.X, _display.Right - Ui.S(24)));
            float y = Math.Max(_display.Y, Math.Min(_rect.Y, _display.Bottom - Ui.S(24)));
            float w = Math.Min(_rect.Width, _display.Right - x);
            float h = Math.Min(_rect.Height, _display.Bottom - y);
            _rect = new RectangleF(x, y, Math.Max(Ui.S(24), w), Math.Max(Ui.S(24), h));
        }

        /// <summary>裁剪框在“当前显示区域”里的归一化矩形，和已有裁剪合成后的像素尺寸。</summary>
        private Size ComposedPixels(ViewerForm form)
        {
            ImageDocument document = form.Document;
            if (document == null || _display.Width <= 0f || _display.Height <= 0f)
            {
                return Size.Empty;
            }
            EditRecipe recipe = form.Recipe;
            float nx = (_rect.X - _display.X) / _display.Width;
            float ny = (_rect.Y - _display.Y) / _display.Height;
            float nw = _rect.Width / _display.Width;
            float nh = _rect.Height / _display.Height;

            float ox = recipe.Crop.X;
            float oy = recipe.Crop.Y;
            float ow = recipe.Crop.Width;
            float oh = recipe.Crop.Height;

            int width = (int)Math.Round(nw * ow * document.PixelWidth);
            int height = (int)Math.Round(nh * oh * document.PixelHeight);
            if (width < 1) { width = 1; }
            if (height < 1) { height = 1; }
            return new Size(width, height);
        }

        internal override void Paint(Graphics g, ViewerForm form)
        {
            Palette p = Theme.P;

            // 框外压暗
            Rectangle client = form.ClientRectangle;
            float left = _rect.X, top = _rect.Y, right = _rect.Right, bottom = _rect.Bottom;
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                g.FillRectangle(dim, client.X, client.Y, client.Width, Math.Max(0f, top - client.Y));
                g.FillRectangle(dim, client.X, bottom, client.Width, Math.Max(0f, client.Bottom - bottom));
                g.FillRectangle(dim, client.X, Math.Max(client.Y, top), Math.Max(0f, left - client.X),
                    Math.Max(0f, bottom - Math.Max(client.Y, top)));
                g.FillRectangle(dim, right, Math.Max(client.Y, top), Math.Max(0f, client.Right - right),
                    Math.Max(0f, bottom - Math.Max(client.Y, top)));
            }

            // 三分线
            using (Pen thirds = new Pen(Color.FromArgb(58, 255, 255, 255), 1f))
            {
                for (int i = 1; i < 3; i++)
                {
                    float x = left + _rect.Width * i / 3f;
                    float y = top + _rect.Height * i / 3f;
                    g.DrawLine(thirds, x, top, x, bottom);
                    g.DrawLine(thirds, left, y, right, y);
                }
            }

            using (Pen border = new Pen(p.Accent, 2f))
            {
                g.DrawRectangle(border, left, top, _rect.Width, _rect.Height);
            }

            // 8 个把手
            float s = Ui.S(9);
            float[] hx = new float[] { left, left + _rect.Width / 2f, right };
            float[] hy = new float[] { top, top + _rect.Height / 2f, bottom };
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(255, 252, 252, 255)))
            using (Pen edge = new Pen(p.Accent, 1.5f))
            {
                for (int iy = 0; iy < 3; iy++)
                {
                    for (int ix = 0; ix < 3; ix++)
                    {
                        if (ix == 1 && iy == 1)
                        {
                            continue;
                        }
                        RectangleF handle = new RectangleF(hx[ix] - s / 2f, hy[iy] - s / 2f, s, s);
                        g.FillRectangle(fill, handle);
                        g.DrawRectangle(edge, handle.X, handle.Y, handle.Width, handle.Height);
                    }
                }
            }

            // 尺寸提示（跟随裁剪框左上角）
            Size pixels = ComposedPixels(form);
            string label = "裁剪后 " + pixels.Width + " × " + pixels.Height;
            Font font = Fonts.Small;
            SizeF textSize = g.MeasureString(label, font);
            Rectangle imageArea = form.GetImageArea();
            float chipTop = top - textSize.Height - Ui.S(12);
            // 裁剪框顶到图片区上沿时，提示块要留在图片区内，否则会画到标题栏文字上
            if (chipTop < imageArea.Y + Ui.S(4))
            {
                chipTop = top + Ui.S(6);
            }
            RectangleF chip = new RectangleF(left, chipTop, textSize.Width + Ui.S(16), textSize.Height + Ui.S(6));
            PaintKit.FillRound(g, chip, Ui.SF(6f), Color.FromArgb(220, 0, 0, 0));
            PaintKit.Text(g, label, font, Color.White, chip, StringAlignment.Center, StringAlignment.Center,
                false);

            // 底部操作条
            PaintKit.FillRound(g, _bar, Ui.SF(12f), Theme.P.Surface);
            PaintKit.StrokeRound(g, _bar, Ui.SF(12f), p.Border, 1f);

            for (int i = 0; i < _presetRects.Length; i++)
            {
                bool active = i == _aspectIndex;
                OverlayPaint.Button(g, _presetRects[i], AspectLabels[i], active, _hotButton == 100 + i, false);
            }

            OverlayPaint.Button(g, _cancelRect, Strings.EditCancel, false, _hotButton == 200, false);
            OverlayPaint.Button(g, _applyRect, Strings.EditApply, true, _hotButton == 201, false);

            PaintKit.Text(g, "拖动边角 · Enter 应用", Fonts.Tiny, p.TextDim,
                new Rectangle(_cancelRect.X - Ui.S(136), _bar.Y, Ui.S(126), _bar.Height), StringAlignment.Far,
                StringAlignment.Center, false);
        }

        // ---------------- 输入 ----------------

        private int HitHandle(Point point)
        {
            int tol = Ui.S(10);
            RectangleF r = _rect;
            bool nearLeft = Math.Abs(point.X - r.Left) <= tol;
            bool nearRight = Math.Abs(point.X - r.Right) <= tol;
            bool nearTop = Math.Abs(point.Y - r.Top) <= tol;
            bool nearBottom = Math.Abs(point.Y - r.Bottom) <= tol;
            bool insideX = point.X >= r.Left - tol && point.X <= r.Right + tol;
            bool insideY = point.Y >= r.Top - tol && point.Y <= r.Bottom + tol;

            if (nearLeft && nearTop) { return HandleTopLeft; }
            if (nearRight && nearTop) { return HandleTopRight; }
            if (nearRight && nearBottom) { return HandleBottomRight; }
            if (nearLeft && nearBottom) { return HandleBottomLeft; }
            if (nearTop && insideX) { return HandleTop; }
            if (nearBottom && insideX) { return HandleBottom; }
            if (nearLeft && insideY) { return HandleLeft; }
            if (nearRight && insideY) { return HandleRight; }
            if (r.Contains(point)) { return HandleMove; }
            return HandleNone;
        }

        internal override bool HandleMouseDown(Point point, ViewerForm form)
        {
            // 先按具体按钮命中：之前只测了第一个预设，导致点其它预设被当成"点在操作条空白处"吞掉
            for (int i = 0; i < _presetRects.Length; i++)
            {
                if (_presetRects[i].Contains(point))
                {
                    ApplyAspect(i, form);
                    return true;
                }
            }
            if (_applyRect.Contains(point))
            {
                Commit(form);
                return true;
            }
            if (_cancelRect.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }
            if (_bar.Contains(point))
            {
                return true;
            }

            int handle = HitHandle(point);
            if (handle == HandleNone)
            {
                return true;
            }
            _dragHandle = handle;
            _dragStart = point;
            _startRect = _rect;
            return true;
        }

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            if (_dragHandle == HandleNone)
            {
                int handle = HitHandle(point);
                Cursor cursor = Cursors.Default;
                if (handle == HandleMove) { cursor = Cursors.SizeAll; }
                else if (handle == HandleTopLeft || handle == HandleBottomRight) { cursor = Cursors.SizeNWSE; }
                else if (handle == HandleTopRight || handle == HandleBottomLeft) { cursor = Cursors.SizeNESW; }
                else if (handle == HandleLeft || handle == HandleRight) { cursor = Cursors.SizeWE; }
                else if (handle == HandleTop || handle == HandleBottom) { cursor = Cursors.SizeNS; }
                if (form.Cursor != cursor)
                {
                    form.Cursor = cursor;
                }
                return false;
            }

            float dx = point.X - _dragStart.X;
            float dy = point.Y - _dragStart.Y;
            float min = Ui.S(24);
            float left = _startRect.Left;
            float top = _startRect.Top;
            float right = _startRect.Right;
            float bottom = _startRect.Bottom;

            if (_dragHandle == HandleMove)
            {
                left += dx;
                right += dx;
                top += dy;
                bottom += dy;
                if (left < _display.Left) { right += _display.Left - left; left = _display.Left; }
                if (right > _display.Right) { left -= right - _display.Right; right = _display.Right; }
                if (top < _display.Top) { bottom += _display.Top - top; top = _display.Top; }
                if (bottom > _display.Bottom) { top -= bottom - _display.Bottom; bottom = _display.Bottom; }
            }
            else
            {
                if (_dragHandle == HandleTopLeft || _dragHandle == HandleLeft)
                {
                    left = Math.Min(left + dx, right - min);
                }
                if (_dragHandle == HandleTopRight || _dragHandle == HandleRight)
                {
                    right = Math.Max(right + dx, left + min);
                }
                if (_dragHandle == HandleTopLeft || _dragHandle == HandleTop)
                {
                    top = Math.Min(top + dy, bottom - min);
                }
                if (_dragHandle == HandleBottomRight || _dragHandle == HandleBottom)
                {
                    bottom = Math.Max(bottom + dy, top + min);
                }

                left = Math.Max(_display.Left, left);
                right = Math.Min(_display.Right, right);
                top = Math.Max(_display.Top, top);
                bottom = Math.Min(_display.Bottom, bottom);

                ApplyAspectToRect(ref left, ref top, ref right, ref bottom);
            }

            _rect = RectangleF.FromLTRB(left, top, right, bottom);
            ClampRect();
            form.Invalidate();
            return true;
        }

        /// <summary>按当前比例约束调整矩形（以拖动方向为准，保持宽高比）。</summary>
        private void ApplyAspectToRect(ref float left, ref float top, ref float right, ref float bottom)
        {
            float[] aspect = Aspects[_aspectIndex];
            if (aspect[0] <= 0f)
            {
                return;
            }
            float target = aspect[0] / aspect[1];
            float width = right - left;
            float height = bottom - top;
            if (width / Math.Max(1f, height) > target)
            {
                width = height * target;
            }
            else
            {
                height = width / target;
            }
            right = Math.Min(_display.Right, left + width);
            bottom = Math.Min(_display.Bottom, top + height);
            left = Math.Max(_display.Left, right - width);
            top = Math.Max(_display.Top, bottom - height);
        }

        internal override bool HandleMouseUp(Point point, ViewerForm form)
        {
            if (_dragHandle != HandleNone)
            {
                _dragHandle = HandleNone;
                return true;
            }
            return false;
        }

        private void ApplyAspect(int index, ViewerForm form)
        {
            _aspectIndex = index;
            float[] aspect = Aspects[index];
            if (aspect[0] <= 0f || _display.Width <= 0f || _display.Height <= 0f)
            {
                form.Invalidate();
                return;
            }
            float target = aspect[0] / aspect[1];
            float width = _display.Width;
            float height = width / target;
            if (height > _display.Height)
            {
                height = _display.Height;
                width = height * target;
            }
            float cx = _display.X + _display.Width / 2f;
            float cy = _display.Y + _display.Height / 2f;
            _rect = new RectangleF(cx - width / 2f, cy - height / 2f, width, height);
            ClampRect();
            form.Invalidate();
        }

        private void Commit(ViewerForm form)
        {
            if (_display.Width <= 0f || _display.Height <= 0f)
            {
                form.SetOverlay(null);
                return;
            }
            EditRecipe recipe = form.Recipe;
            float nx = Math.Max(0f, (_rect.X - _display.X) / _display.Width);
            float ny = Math.Max(0f, (_rect.Y - _display.Y) / _display.Height);
            float nw = Math.Min(1f - nx, _rect.Width / _display.Width);
            float nh = Math.Min(1f - ny, _rect.Height / _display.Height);

            RectangleF composed = new RectangleF(
                recipe.Crop.X + nx * recipe.Crop.Width,
                recipe.Crop.Y + ny * recipe.Crop.Height,
                nw * recipe.Crop.Width,
                nh * recipe.Crop.Height);
            recipe.Crop = composed;
            form.SetOverlay(null);
            form.OnRecipeChanged(true);
        }

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Enter)
            {
                Commit(form);
                return true;
            }
            if (keyData == Keys.Escape)
            {
                form.SetOverlay(null);
                return true;
            }
            int step = (keyData & Keys.Shift) == Keys.Shift ? Ui.S(10) : 1;
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
            {
                float dx = key == Keys.Left ? -step : key == Keys.Right ? step : 0;
                float dy = key == Keys.Up ? -step : key == Keys.Down ? step : 0;
                _rect = new RectangleF(_rect.X + dx, _rect.Y + dy, _rect.Width, _rect.Height);
                ClampRect();
                form.Invalidate();
                return true;
            }
            return false;
        }
    }
}
