using System;
using System.Drawing;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>设置浮层：外观主题、缩略图条、文件关联登记、关于信息。</summary>
    internal sealed class SettingsOverlay : Overlay
    {
        private Rectangle _card;
        private readonly Rectangle[] _themeRects = new Rectangle[3];
        private Rectangle _filmstripRect;
        private Rectangle _registerRect;
        private Rectangle _unregisterRect;
        private Rectangle _defaultAppRect;
        private Rectangle _doneRect;
        private int _hot = -1;
        private string _status;

        internal SettingsOverlay()
        {
        }

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            _card = OverlayPaint.CenterCard(client, Ui.S(560), Ui.S(470));
            int pad = Ui.S(24);
            int left = _card.X + pad;
            int right = _card.Right - pad;

            int themeY = _card.Y + Ui.S(96);
            int chipW = Ui.S(96);
            int chipH = Ui.S(30);
            for (int i = 0; i < 3; i++)
            {
                _themeRects[i] = new Rectangle(left + i * (chipW + Ui.S(8)), themeY, chipW, chipH);
            }

            _filmstripRect = new Rectangle(left, themeY + chipH + Ui.S(34), Ui.S(36), Ui.S(20));

            int assocY = _filmstripRect.Bottom + Ui.S(48);
            _registerRect = new Rectangle(left, assocY, Ui.S(112), Ui.S(32));
            _unregisterRect = new Rectangle(_registerRect.Right + Ui.S(8), assocY, Ui.S(112), Ui.S(32));
            _defaultAppRect = new Rectangle(_unregisterRect.Right + Ui.S(8), assocY, Ui.S(160), Ui.S(32));

            _doneRect = new Rectangle(right - Ui.S(96), _card.Bottom - pad - Ui.S(34), Ui.S(96), Ui.S(34));
        }

        internal override void Paint(Graphics g, ViewerForm form)
        {
            Palette p = Theme.P;
            OverlayPaint.Scrim(g, form);
            if (_card.IsEmpty)
            {
                Layout(form, form.ClientRectangle);
            }
            OverlayPaint.Card(g, _card, Strings.SettingsTitle, "ImageView " + Strings.Version + " · " +
                            Formats.SupportedExtensionsText);

            int pad = Ui.S(24);
            int left = _card.X + pad;
            int right = _card.Right - pad;

            // 外观
            PaintKit.Text(g, Strings.SettingsTheme, Fonts.Body, p.Text,
                new Rectangle(left, _themeRects[0].Y - Ui.S(24), Ui.S(200), Ui.S(20)), StringAlignment.Near,
                StringAlignment.Center, false);
            string[] labels = new string[] { Strings.MenuThemeSystem, Strings.MenuThemeLight, Strings.MenuThemeDark };
            for (int i = 0; i < 3; i++)
            {
                bool selected = (int)Theme.Mode == i;
                PaintChip(g, _themeRects[i], labels[i], selected, _hot == i);
            }

            // 缩略图条
            bool filmstrip = Settings.Current.Filmstrip;
            PaintSwitch(g, _filmstripRect, filmstrip, _hot == 10);
            PaintKit.Text(g, Strings.SettingsFilmstrip, Fonts.Body, p.Text,
                new Rectangle(_filmstripRect.Right + Ui.S(12), _filmstripRect.Y - Ui.S(4), Ui.S(320), Ui.S(28)),
                StringAlignment.Near, StringAlignment.Center, false);

            // 文件关联
            PaintKit.Text(g, Strings.SettingsAssoc, Fonts.Body, p.Text,
                new Rectangle(left, _registerRect.Y - Ui.S(46), Ui.S(200), Ui.S(20)), StringAlignment.Near,
                StringAlignment.Center, false);
            PaintKit.Text(g, Strings.SettingsAssocDesc, Fonts.Small, p.TextDim,
                new Rectangle(left, _registerRect.Y - Ui.S(26), right - left, Ui.S(20)), StringAlignment.Near,
                StringAlignment.Center, true);

            bool registered = Assoc.IsRegistered();
            OverlayPaint.Button(g, _registerRect, Strings.SettingsRegister, !registered, _hot == 20, false);
            OverlayPaint.Button(g, _unregisterRect, Strings.SettingsUnregister, false, _hot == 21, true);
            OverlayPaint.Button(g, _defaultAppRect, Strings.SettingsSetDefault, false, _hot == 22, false);

            if (!string.IsNullOrEmpty(_status))
            {
                PaintKit.Text(g, _status, Fonts.Small, p.TextDim,
                    new Rectangle(left, _registerRect.Bottom + Ui.S(8), right - left, Ui.S(40)),
                    StringAlignment.Near, StringAlignment.Near, true);
            }

            // 配置文件位置
            PaintKit.Text(g, Strings.SettingsPath + "：" + Settings.FilePath, Fonts.Tiny, p.TextDim,
                new Rectangle(left, _doneRect.Y - Ui.S(26), right - left - Ui.S(110), Ui.S(20)), StringAlignment.Near,
                StringAlignment.Center, true);

            OverlayPaint.Button(g, _doneRect, Strings.SettingsClose, true, _hot == 30, false);
        }

        private void PaintChip(Graphics g, Rectangle rect, string text, bool selected, bool hot)
        {
            Palette p = Theme.P;
            Color fill = selected ? p.AccentSoft : (hot ? p.SurfaceAlt : p.SurfaceSunken);
            Color ink = selected ? p.Accent : (hot ? p.Text : p.TextDim);
            PaintKit.FillRound(g, rect, Ui.SF(7f), fill);
            if (selected || hot)
            {
                PaintKit.StrokeRound(g, rect, Ui.SF(7f), Color.FromArgb(selected ? 160 : 90, p.Accent), 1f);
            }
            PaintKit.Text(g, text, Fonts.Small, ink, rect, StringAlignment.Center, StringAlignment.Center, false);
        }

        private void PaintSwitch(Graphics g, Rectangle rect, bool on, bool hot)
        {
            Palette p = Theme.P;
            Color track = on ? p.Accent : (hot ? p.BorderStrong : p.Border);
            PaintKit.FillRound(g, rect, rect.Height / 2f, track);
            int knob = rect.Height - Ui.S(4);
            int x = on ? rect.Right - knob - Ui.S(2) : rect.X + Ui.S(2);
            PaintKit.FillRound(g, new Rectangle(x, rect.Y + Ui.S(2), knob, knob), knob / 2f, Color.White);
        }

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            int hot = -1;
            for (int i = 0; i < 3; i++)
            {
                if (_themeRects[i].Contains(point))
                {
                    hot = i;
                }
            }
            if (_filmstripRect.Contains(point))
            {
                hot = 10;
            }
            if (_registerRect.Contains(point))
            {
                hot = 20;
            }
            if (_unregisterRect.Contains(point))
            {
                hot = 21;
            }
            if (_defaultAppRect.Contains(point))
            {
                hot = 22;
            }
            if (_doneRect.Contains(point))
            {
                hot = 30;
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
            if (!_card.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }
            for (int i = 0; i < 3; i++)
            {
                if (_themeRects[i].Contains(point))
                {
                    Theme.Mode = (ThemeMode)i;
                    Settings.Current.Theme = Theme.Mode;
                    Settings.Current.Save();
                    return true;
                }
            }
            if (_filmstripRect.Contains(point))
            {
                Settings.Current.Filmstrip = !Settings.Current.Filmstrip;
                Settings.Current.Save();
                form.ApplyFilmstripSetting();
                return true;
            }
            if (_registerRect.Contains(point))
            {
                string error;
                _status = Assoc.Register(out error)
                    ? Strings.ToastAssocRegistered
                    : (Strings.ResizeFailed + "：" + error);
                return true;
            }
            if (_unregisterRect.Contains(point))
            {
                string error;
                _status = Assoc.Unregister(out error) ? Strings.ToastAssocRemoved : error;
                return true;
            }
            if (_defaultAppRect.Contains(point))
            {
                _status = Assoc.OpenDefaultAppsSettings() ? "已打开「默认应用」设置页" : "无法打开设置页";
                return true;
            }
            if (_doneRect.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }
            return true;
        }

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Escape || keyData == Keys.Enter)
            {
                form.SetOverlay(null);
                return true;
            }
            return false;
        }
    }
}
