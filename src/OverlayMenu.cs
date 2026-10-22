using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>右键 / 更多按钮弹出的自绘菜单。</summary>
    internal sealed class ContextMenuOverlay : Overlay
    {
        private sealed class Item
        {
            internal string Text;
            internal string Shortcut;
            internal bool Separator;
            internal Action<ViewerForm> Run;
        }

        private const int RowHeight = 32;
        private const int SeparatorHeight = 9;
        private const int Width = 268;

        private readonly List<Item> _items = new List<Item>();
        private readonly Point _anchor;
        private Rectangle _bounds;
        private int _hot = -1;

        internal ContextMenuOverlay(Point anchor)
        {
            _anchor = anchor;
            Build();
        }

        private void Build()
        {
            Add("复制图片", "Ctrl+C", delegate(ViewerForm f) { f.CopyImageToClipboard(); });
            Add("裁剪…", "Ctrl+K", delegate(ViewerForm f) { f.ShowCropOverlay(); });
            Add("亮度 / 对比度…", "Ctrl+E", delegate(ViewerForm f) { f.ShowAdjustOverlay(); });
            Add("另存编辑副本", "Ctrl+S", delegate(ViewerForm f) { f.SaveEditedCopy(); });
            Add("调整尺寸…", "Ctrl+R", delegate(ViewerForm f) { f.ShowResizeOverlay(); });
            Add("批量另存…", "Ctrl+B", delegate(ViewerForm f) { f.ShowBatchOverlay(); });
            Separator();
            Add("设为桌面壁纸", null, delegate(ViewerForm f)
            {
                string path = f.DocumentPath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                if (Native.SetWallpaper(path))
                {
                    f.ShowToast(Strings.ToastWallpaper);
                }
                else
                {
                    f.ShowToast(Strings.ToastWallpaperFailed);
                }
            });
            Add("在资源管理器中显示", null, delegate(ViewerForm f) { RevealInExplorer(f.DocumentPath); });
            Add("图片信息", null, delegate(ViewerForm f) { f.ShowInfoOverlay(); });
            Separator();
            Add("删除到回收站", "Del", delegate(ViewerForm f) { f.RequestDelete(); });
            Separator();
            AddTheme();
            Separator();
            Add("全屏", "F11", delegate(ViewerForm f) { f.ToggleFullscreen(); });
            Add("设为默认打开方式…", null, delegate(ViewerForm f) { f.SetOverlay(new SettingsOverlay()); });
            Add("设置…", "Ctrl+,", delegate(ViewerForm f) { f.ShowSettingsOverlay(); });
        }

        private void Add(string text, string shortcut, Action<ViewerForm> run)
        {
            Item item = new Item();
            item.Text = text;
            item.Shortcut = shortcut;
            item.Run = run;
            _items.Add(item);
        }

        private void AddTheme()
        {
            Item item = new Item();
            item.Text = "外观：" + Theme.Label(Theme.Mode);
            item.Shortcut = "Ctrl+T";
            item.Run = delegate(ViewerForm f) { f.CycleTheme(); };
            _items.Add(item);
        }


        private void Separator()
        {
            Item item = new Item();
            item.Separator = true;
            _items.Add(item);
        }

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            int height = Ui.S(12);
            for (int i = 0; i < _items.Count; i++)
            {
                height += _items[i].Separator ? Ui.S(SeparatorHeight) : Ui.S(RowHeight);
            }
            int width = Ui.S(Width);

            Rectangle area = form.GetImageArea();
            int x = _anchor.X;
            int y = _anchor.Y - height;
            if (x + width > area.Right - Ui.S(8))
            {
                x = area.Right - Ui.S(8) - width;
            }
            if (x < area.X + Ui.S(8))
            {
                x = area.X + Ui.S(8);
            }
            if (y < area.Y + Ui.S(8))
            {
                y = Math.Min(area.Y + Ui.S(8), _anchor.Y + Ui.S(8));
            }
            if (y + height > area.Bottom - Ui.S(8))
            {
                y = Math.Max(area.Y + Ui.S(8), area.Bottom - Ui.S(8) - height);
            }
            _bounds = new Rectangle(x, y, width, height);
        }

        private Rectangle RowRect(int index)
        {
            int y = _bounds.Y + Ui.S(6);
            for (int i = 0; i < index; i++)
            {
                y += _items[i].Separator ? Ui.S(SeparatorHeight) : Ui.S(RowHeight);
            }
            return new Rectangle(_bounds.X + Ui.S(6), y,
                _bounds.Width - Ui.S(12), _items[index].Separator ? Ui.S(SeparatorHeight) : Ui.S(RowHeight));
        }

        internal override void Paint(Graphics g, ViewerForm form)
        {
            if (_bounds.IsEmpty)
            {
                Layout(form, form.ClientRectangle);
            }
            Palette p = Theme.P;
            float radius = Ui.SF(10f);
            PaintKit.Shadow(g, _bounds, radius, Ui.S(9), p.Dark ? 150 : 70);
            PaintKit.FillRound(g, _bounds, radius, p.Surface);
            PaintKit.StrokeRound(g, _bounds, radius, Color.FromArgb(160, p.BorderStrong), 1f);

            for (int i = 0; i < _items.Count; i++)
            {
                Item item = _items[i];
                Rectangle row = RowRect(i);
                if (item.Separator)
                {
                    int y = row.Y + row.Height / 2;
                    g.DrawLine(PaintKit.PenFor(Color.FromArgb(p.Dark ? 40 : 60, p.BorderStrong), 1f),
                        row.X + Ui.S(2), y, row.Right - Ui.S(2), y);
                    continue;
                }

                if (_hot == i)
                {
                    PaintKit.FillRound(g, row, Ui.SF(6f), p.SurfaceAlt);
                }

                // 先给快捷键留出宽度再画标签：否则长标签会一直排到右边、被快捷键字形盖住
                int shortcutWidth = 0;
                if (!string.IsNullOrEmpty(item.Shortcut))
                {
                    shortcutWidth = (int)Math.Ceiling(PaintKit.MeasureWidth(g, item.Shortcut, Fonts.Small)) +
                                    Ui.S(10);
                }

                Rectangle textRect = new Rectangle(row.X + Ui.S(12), row.Y,
                    row.Width - Ui.S(24) - shortcutWidth, row.Height);
                PaintKit.Text(g, item.Text, Fonts.Body, p.Text, textRect, StringAlignment.Near,
                    StringAlignment.Center, true);

                if (shortcutWidth > 0)
                {
                    Rectangle scRect = new Rectangle(row.Right - shortcutWidth, row.Y, shortcutWidth, row.Height);
                    PaintKit.Text(g, item.Shortcut, Fonts.Small, p.TextDim, scRect, StringAlignment.Far,
                        StringAlignment.Center, false);
                }
            }
        }

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            int hot = -1;
            if (_bounds.Contains(point))
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!_items[i].Separator && RowRect(i).Contains(point))
                    {
                        hot = i;
                        break;
                    }
                }
            }
            if (hot != _hot)
            {
                _hot = hot;
                return true;
            }
            return false;
        }

        internal override bool HandleMouseDown(Point point, ViewerForm form)
        {
            if (!_bounds.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Separator || !RowRect(i).Contains(point))
                {
                    continue;
                }
                Action<ViewerForm> run = _items[i].Run;
                form.SetOverlay(null);
                if (run != null)
                {
                    run(form);
                }
                return true;
            }
            return true;
        }

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Escape)
            {
                form.SetOverlay(null);
                return true;
            }
            return false;
        }

        private static void RevealInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"");
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                AppLog.Error("RevealInExplorer", ex);
            }
        }
    }
}
