using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>
    /// 底部缩略图条（按参考图重排）：不再是一整条背景，而是把缩略图直接排在页面底色上，
    /// 选中项用强调色描边。只绘制可见范围，缩略图由后台线程解码。
    /// </summary>
    internal sealed class Filmstrip
    {
        internal const int LogicalHeight = 88;
        private const int CellWidth = 96;
        private const int CellHeight = 64;

        private FolderSource _folder;
        private ThumbnailCache _thumbs;

        private Rectangle _bounds;
        private int _thumbWidth;
        private int _thumbHeight;
        private int _gap;
        private int _pad;
        private int _scroll;
        private int _selected = -1;

        internal bool Visible { get; set; }

        internal Rectangle Bounds
        {
            get { return _bounds; }
        }

        internal int Selected
        {
            get { return _selected; }
            set { _selected = value; }
        }

        internal static int Height
        {
            get { return Ui.S(LogicalHeight); }
        }

        internal void Attach(FolderSource folder, ThumbnailCache thumbs)
        {
            _folder = folder;
            _thumbs = thumbs;
            _scroll = 0;
        }

        internal void Layout(Rectangle stripRect)
        {
            _bounds = stripRect;
            _thumbWidth = Ui.S(CellWidth);
            _thumbHeight = Ui.S(CellHeight);
            _gap = Ui.S(10);
            _pad = Ui.S(20);
            ClampScroll();
        }

        private int Step
        {
            get { return _thumbWidth + _gap; }
        }

        private int ContentWidth
        {
            get
            {
                if (_folder == null || _folder.Count == 0)
                {
                    return 0;
                }
                return _folder.Count * Step - _gap;
            }
        }

        private int OriginX
        {
            get
            {
                int content = ContentWidth;
                if (content + _pad * 2 <= _bounds.Width)
                {
                    return _bounds.X + (_bounds.Width - content) / 2;
                }
                return _bounds.X + _pad - _scroll;
            }
        }

        private void ClampScroll()
        {
            int content = ContentWidth;
            int viewport = _bounds.Width - _pad * 2;
            if (content <= viewport)
            {
                _scroll = 0;
                return;
            }
            int max = content - viewport;
            if (_scroll < 0)
            {
                _scroll = 0;
            }
            if (_scroll > max)
            {
                _scroll = max;
            }
        }

        internal void EnsureVisible(int index)
        {
            if (_folder == null || _folder.Count == 0)
            {
                return;
            }
            if (ContentWidth <= _bounds.Width - _pad * 2)
            {
                return;
            }
            int viewport = _bounds.Width - _pad * 2;
            int x = index * Step;
            if (x < _scroll)
            {
                _scroll = x;
            }
            else if (x + _thumbWidth > _scroll + viewport)
            {
                _scroll = x + _thumbWidth - viewport;
            }
            ClampScroll();
        }

        internal void ScrollBy(int delta)
        {
            _scroll += delta;
            ClampScroll();
        }

        internal int HitTest(Point point)
        {
            if (!Visible || _folder == null || _folder.Count == 0 || !_bounds.Contains(point))
            {
                return -1;
            }
            int x = point.X - OriginX;
            if (x < 0)
            {
                return -1;
            }
            int index = x / Step;
            if (index < 0 || index >= _folder.Count)
            {
                return -1;
            }
            if (x - index * Step > _thumbWidth)
            {
                return -1;
            }
            return CellRect(index).Contains(point) ? index : -1;
        }

        private Rectangle CellRect(int index)
        {
            int y = _bounds.Y + (_bounds.Height - _thumbHeight) / 2;
            return new Rectangle(OriginX + index * Step, y, _thumbWidth, _thumbHeight);
        }

        internal void Paint(Graphics g)
        {
            if (!Visible || _bounds.Height <= 0 || _folder == null || _folder.Count == 0)
            {
                return;
            }

            int viewport = _bounds.Width - _pad * 2;
            int first = Math.Max(0, _scroll / Step - 1);
            int last = Math.Min(_folder.Count - 1, first + viewport / Step + 2);

            for (int i = first; i <= last; i++)
            {
                Rectangle cell = CellRect(i);
                if (cell.Right < _bounds.X || cell.X > _bounds.Right)
                {
                    continue;
                }
                PaintCell(g, cell, i);
            }
        }

        private void PaintCell(Graphics g, Rectangle cell, int index)
        {
            Palette p = Theme.P;
            string path = _folder.PathAt(index);
            Bitmap thumb = _thumbs == null || path == null ? null : _thumbs.TryGet(path);
            float radius = Ui.SF(8f);
            bool selected = index == _selected;

            if (thumb == null)
            {
                PaintKit.FillRound(g, cell, radius, p.SurfaceAlt);
                int s = Ui.S(18);
                PaintKit.Icon(g, IconId.Image, new Rectangle(cell.X + (cell.Width - s) / 2,
                    cell.Y + (cell.Height - s) / 2, s, s), p.TextDim, 1.2f);
                if (path != null && _thumbs != null)
                {
                    _thumbs.Request(path);
                }
            }
            else
            {
                GraphicsState state = g.Save();
                try
                {
                    using (GraphicsPath clip = PaintKit.RoundRect(cell, radius))
                    {
                        g.SetClip(clip, CombineMode.Intersect);
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(thumb, FitInside(thumb.Width, thumb.Height, cell));
                    }
                }
                finally
                {
                    g.Restore(state);
                }
            }

            // 选中：2px 强调色描边；其它：极淡的 1px 描边
            if (selected)
            {
                PaintKit.StrokeRound(g, cell, radius, p.Accent, Math.Max(2f, Ui.SF(2f)));
            }
            else
            {
                PaintKit.StrokeRound(g, cell, radius, Color.FromArgb(p.Dark ? 40 : 60, p.BorderStrong), 1f);
            }
        }

        private static RectangleF FitInside(int sourceWidth, int sourceHeight, Rectangle cell)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return cell;
            }
            float scale = Math.Min((float)cell.Width / sourceWidth, (float)cell.Height / sourceHeight);
            float w = sourceWidth * scale;
            float h = sourceHeight * scale;
            return new RectangleF(cell.X + (cell.Width - w) / 2f, cell.Y + (cell.Height - h) / 2f, w, h);
        }
    }
}
