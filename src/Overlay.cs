using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImageView
{
    /// <summary>
    /// 浮层基类：菜单、确认、调整尺寸、设置都用同一套约定。
    /// 返回 true 表示事件已被消费；未消费的事件会继续交给窗体处理（标题栏始终可用）。
    /// </summary>
    internal abstract class Overlay
    {
        internal virtual void Layout(ViewerForm form, Rectangle client)
        {
        }

        /// <summary>关闭时清理（例如移除宿主窗体上的原生输入框）。</summary>
        internal virtual void Detach(ViewerForm form)
        {
        }

        internal abstract void Paint(Graphics g, ViewerForm form);

        internal virtual bool HandleKey(Keys keyData, ViewerForm form)
        {
            return false;
        }

        internal virtual bool HandleMouseDown(Point point, ViewerForm form)
        {
            return false;
        }

        internal virtual bool HandleMouseUp(Point point, ViewerForm form)
        {
            return false;
        }

        internal virtual bool HandleMouseMove(Point point, ViewerForm form)
        {
            return false;
        }

        /// <summary>滚轮（信息面板这类可滚动的浮层用）。返回 true 表示已消费。</summary>
        internal virtual bool HandleWheel(int delta, Point point, ViewerForm form)
        {
            return false;
        }

        internal virtual bool BlocksInput
        {
            get { return true; }
        }
    }

    /// <summary>浮层通用绘制：压暗层 + 居中卡片 + 标题。</summary>
    internal static class OverlayPaint
    {
        internal static void Scrim(Graphics g, ViewerForm form)
        {
            Rectangle area = form.GetImageArea();
            Palette p = Theme.P;
            g.FillRectangle(PaintKit.Brush(Color.FromArgb(p.Dark ? 150 : 110, 0, 0, 0)), area);
        }

        internal static void Card(Graphics g, Rectangle card, string title, string subtitle)
        {
            Palette p = Theme.P;
            float radius = Ui.SF(14f);
            PaintKit.Shadow(g, card, radius, Ui.S(10), p.Dark ? 150 : 80);
            PaintKit.FillRound(g, card, radius, p.Surface);
            PaintKit.StrokeRound(g, card, radius, Color.FromArgb(150, p.BorderStrong), 1f);

            int pad = Ui.S(22);
            int y = card.Y + pad;
            if (!string.IsNullOrEmpty(title))
            {
                Rectangle titleRect = new Rectangle(card.X + pad, y, card.Width - pad * 2, Ui.S(28));
                PaintKit.Text(g, title, Fonts.Large, p.Text, titleRect, StringAlignment.Near, StringAlignment.Center,
                    true);
                y += Ui.S(30);
            }
            if (!string.IsNullOrEmpty(subtitle))
            {
                Rectangle subRect = new Rectangle(card.X + pad, y, card.Width - pad * 2, Ui.S(22));
                PaintKit.Text(g, subtitle, Fonts.Small, p.TextDim, subRect, StringAlignment.Near,
                    StringAlignment.Center, true);
            }
        }

        internal static Rectangle CenterCard(Rectangle client, int width, int height)
        {
            int w = Math.Min(width, Math.Max(240, client.Width - Ui.S(40)));
            int h = Math.Min(height, Math.Max(120, client.Height - Ui.S(40)));
            return new Rectangle(client.X + (client.Width - w) / 2, client.Y + (client.Height - h) / 2, w, h);
        }

        /// <summary>按钮：filled 为主按钮。</summary>
        internal static void Button(Graphics g, Rectangle rect, string text, bool filled, bool hot, bool danger)
        {
            Palette p = Theme.P;
            float radius = Ui.SF(8f);
            Color fill;
            Color ink;
            if (filled)
            {
                fill = danger ? p.Danger : p.Accent;
                if (hot)
                {
                    fill = danger ? Lighten(p.Danger, 0.1f) : Lighten(p.Accent, 0.1f);
                }
                ink = Color.White;
            }
            else
            {
                fill = hot ? p.SurfaceAlt : Color.FromArgb(0, p.Surface);
                ink = danger ? p.Danger : p.Text;
            }

            if (filled || hot)
            {
                PaintKit.FillRound(g, rect, radius, fill);
            }
            PaintKit.StrokeRound(g, rect, radius, danger ? Color.FromArgb(120, p.Danger) : Color.FromArgb(120, p.BorderStrong),
                1f);
            PaintKit.Text(g, text, Fonts.Body, ink, rect, StringAlignment.Center, StringAlignment.Center, false);
        }

        internal static Color Lighten(Color color, float amount)
        {
            int r = Math.Min(255, (int)(color.R + (255 - color.R) * amount));
            int g = Math.Min(255, (int)(color.G + (255 - color.G) * amount));
            int b = Math.Min(255, (int)(color.B + (255 - color.B) * amount));
            return Color.FromArgb(color.A, r, g, b);
        }

        internal static Color Darken(Color color, float amount)
        {
            return Color.FromArgb(color.A, (int)(color.R * (1 - amount)), (int)(color.G * (1 - amount)),
                (int)(color.B * (1 - amount)));
        }
    }
}
