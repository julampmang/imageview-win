using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ImageView
{
    /// <summary>
    /// 底部悬浮工具条（按参考图重排）：
    ///   [缩小] [比例] [放大] │ [适应] [1:1] [缩略图] │ [更多] [删除]
    /// 当前生效的那一档（适应窗口等）用强调色实心圆角块表示。
    /// </summary>
    internal sealed class Toolbar
    {
        internal enum Command
        {
            None = 0,
            ZoomOut,
            ZoomIn,
            Fit,
            Actual,
            Filmstrip,
            Menu,
            Delete
        }

        private sealed class Item
        {
            internal Command Command;
            internal IconId Icon;
            internal string Text;
            internal Rectangle Bounds;
            internal bool SeparatorBefore;
        }

        private readonly List<Item> _items = new List<Item>();
        private Rectangle _bounds;

        internal int HotIndex = -1;
        internal int PressedIndex = -1;
        internal double Scale = 1.0;
        internal bool FitActive = true;
        internal bool ActualActive;
        internal bool FilmstripActive = true;
        internal bool Visible { get; set; }

        internal Rectangle Bounds
        {
            get { return _bounds; }
        }

        internal void Layout(Rectangle area)
        {
            _items.Clear();

            int h = Ui.S(44);
            int pad = Ui.S(8);
            int btn = Ui.S(36);
            int zoom = Ui.S(56);
            int sep = Ui.S(12);

            int total = pad * 2 + btn + zoom + btn + sep + btn + btn + btn + sep + btn + btn;

            int x = area.X + (area.Width - total) / 2;
            if (x < area.X + Ui.S(8))
            {
                x = area.X + Ui.S(8);
            }
            int y = area.Bottom - h - Ui.S(6);
            _bounds = new Rectangle(x, y, total, h);

            int cursor = x;
            int cy = y;
            int itemH = h;

            AddItem(Command.ZoomOut, IconId.ZoomOut, null, ref cursor, cy, btn + pad, itemH, false);
            AddItem(Command.None, IconId.None, null, ref cursor, cy, zoom, itemH, false); // 比例
            AddItem(Command.ZoomIn, IconId.ZoomIn, null, ref cursor, cy, btn + pad, itemH, false);
            AddItem(Command.Fit, IconId.Fit, null, ref cursor, cy, btn, itemH, true);
            AddItem(Command.Actual, IconId.None, "1:1", ref cursor, cy, btn, itemH, false);
            AddItem(Command.Filmstrip, IconId.Filmstrip, null, ref cursor, cy, btn, itemH, false);
            AddItem(Command.Menu, IconId.More, null, ref cursor, cy, btn, itemH, true);
            AddItem(Command.Delete, IconId.Trash, null, ref cursor, cy, btn, itemH, false);
        }

        private void AddItem(Command command, IconId icon, string text, ref int cursor, int y, int width, int height,
            bool separatorBefore)
        {
            if (separatorBefore)
            {
                cursor += Ui.S(12);
            }
            Item item = new Item();
            item.Command = command;
            item.Icon = icon;
            item.Text = text;
            item.SeparatorBefore = separatorBefore;
            item.Bounds = new Rectangle(cursor, y + Ui.S(4), width, height - Ui.S(8));
            _items.Add(item);
            cursor += width;
        }

        internal Command HitTest(Point point, out int index)
        {
            index = -1;
            if (!Visible || !_bounds.Contains(point))
            {
                return Command.None;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Bounds.Contains(point))
                {
                    index = i;
                    return _items[i].Command;
                }
            }
            return Command.None;
        }

        internal bool ContainsPoint(Point point)
        {
            return Visible && _bounds.Contains(point);
        }

        internal void Paint(Graphics g)
        {
            if (!Visible)
            {
                return;
            }
            Palette p = Theme.P;
            float radius = _bounds.Height / 2f;

            PaintKit.Shadow(g, _bounds, radius, Ui.S(8), p.Dark ? 120 : 42);
            PaintKit.FillRound(g, _bounds, radius, p.Surface);
            PaintKit.StrokeRound(g, _bounds, radius, Color.FromArgb(p.Dark ? 70 : 120, p.Border), 1f);

            for (int i = 0; i < _items.Count; i++)
            {
                Item item = _items[i];
                if (item.SeparatorBefore)
                {
                    int sx = item.Bounds.X - Ui.S(6);
                    int top = item.Bounds.Y + Ui.S(9);
                    int bottom = item.Bounds.Bottom - Ui.S(9);
                    g.DrawLine(PaintKit.PenFor(p.Border, 1f), sx, top, sx, bottom);
                }

                if (item.Command == Command.None)
                {
                    PaintScaleLabel(g, item.Bounds);
                    continue;
                }

                bool active = IsActive(item.Command);
                bool hot = HotIndex == i && !active;
                bool pressed = PressedIndex == i;

                if (active || hot || pressed)
                {
                    int d = Ui.S(30);
                    Rectangle block = new Rectangle(item.Bounds.X + (item.Bounds.Width - d) / 2,
                        item.Bounds.Y + (item.Bounds.Height - d) / 2, d, d);
                    Color fill = active ? p.Accent : (pressed ? p.SurfaceSunken : p.SurfaceAlt);
                    PaintKit.FillRound(g, block, Ui.SF(9f), fill);
                }

                Color ink;
                if (active)
                {
                    ink = p.OnAccent;
                }
                else if (item.Command == Command.Delete && (hot || pressed))
                {
                    ink = p.Danger;
                }
                else
                {
                    ink = p.Text;
                }

                if (item.Text != null)
                {
                    PaintKit.Text(g, item.Text, Fonts.Small, ink, item.Bounds, StringAlignment.Center,
                        StringAlignment.Center, false);
                }
                else
                {
                    int size = Ui.S(17);
                    Rectangle iconRect = new Rectangle(item.Bounds.X + (item.Bounds.Width - size) / 2,
                        item.Bounds.Y + (item.Bounds.Height - size) / 2, size, size);
                    PaintKit.Icon(g, item.Icon, iconRect, ink, Math.Max(1f, Ui.SF(1.35f)));
                }
            }
        }

        private bool IsActive(Command command)
        {
            switch (command)
            {
                case Command.Fit:
                    return FitActive;
                case Command.Actual:
                    return ActualActive;
                case Command.Filmstrip:
                    return FilmstripActive;
                default:
                    return false;
            }
        }

        private void PaintScaleLabel(Graphics g, Rectangle bounds)
        {
            Palette p = Theme.P;
            PaintKit.Text(g, FormatScale(Scale), Fonts.Small, p.Text, bounds, StringAlignment.Center,
                StringAlignment.Center, false);
        }

        internal static string FormatScale(double scale)
        {
            double percent = scale * 100.0;
            if (percent < 10)
            {
                return percent.ToString("0.#") + "%";
            }
            return percent.ToString("0") + "%";
        }

        internal Rectangle GetItemBounds(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                return Rectangle.Empty;
            }
            return _items[index].Bounds;
        }
    }
}
