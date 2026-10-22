using System;
using System.Drawing;
using System.Windows.Forms;

namespace ImageView
{
    /// <summary>通用确认浮层（删除到回收站、覆盖原文件等）。</summary>
    internal sealed class ConfirmOverlay : Overlay
    {
        private readonly string _title;
        private readonly string _message;
        private readonly string _confirmText;
        private readonly string _cancelText;
        private readonly Action _confirmed;
        private readonly bool _danger;

        private Rectangle _card;
        private Rectangle _confirmRect;
        private Rectangle _cancelRect;
        private int _hot = -1;

        internal ConfirmOverlay(string title, string message, string confirmText, string cancelText, Action confirmed)
        {
            _title = title;
            _message = message;
            _confirmText = confirmText;
            _cancelText = cancelText;
            _confirmed = confirmed;
            _danger = true;
        }

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            _card = OverlayPaint.CenterCard(client, Ui.S(420), Ui.S(210));
            int btnW = Ui.S(96);
            int btnH = Ui.S(34);
            int pad = Ui.S(22);
            _confirmRect = new Rectangle(_card.Right - pad - btnW, _card.Bottom - pad - btnH, btnW, btnH);
            _cancelRect = new Rectangle(_confirmRect.X - Ui.S(10) - btnW, _confirmRect.Y, btnW, btnH);
        }

        internal override void Paint(Graphics g, ViewerForm form)
        {
            OverlayPaint.Scrim(g, form);
            if (_card.IsEmpty)
            {
                Layout(form, form.ClientRectangle);
            }
            OverlayPaint.Card(g, _card, _title, null);

            Palette p = Theme.P;
            int pad = Ui.S(22);
            Rectangle msgRect = new Rectangle(_card.X + pad, _card.Y + Ui.S(60), _card.Width - pad * 2,
                _card.Bottom - _confirmRect.Y - Ui.S(24));
            PaintKit.Text(g, _message, Fonts.Body, p.TextDim, msgRect, StringAlignment.Near, StringAlignment.Near,
                true);

            OverlayPaint.Button(g, _cancelRect, _cancelText, false, _hot == 1, false);
            OverlayPaint.Button(g, _confirmRect, _confirmText, true, _hot == 0, _danger);
        }

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            int hot = -1;
            if (_confirmRect.Contains(point))
            {
                hot = 0;
            }
            else if (_cancelRect.Contains(point))
            {
                hot = 1;
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
            if (_confirmRect.Contains(point))
            {
                form.SetOverlay(null);
                if (_confirmed != null)
                {
                    _confirmed();
                }
                return true;
            }
            if (_cancelRect.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }
            if (_card.Contains(point))
            {
                return true;
            }
            form.SetOverlay(null);
            return true;
        }

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Enter)
            {
                form.SetOverlay(null);
                if (_confirmed != null)
                {
                    _confirmed();
                }
                return true;
            }
            if (keyData == Keys.Escape)
            {
                form.SetOverlay(null);
                return true;
            }
            return false;
        }
    }
}
