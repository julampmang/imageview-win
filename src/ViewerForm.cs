using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>
    /// 主窗体。布局参照设计稿：
    ///   标题栏（62）：左侧应用标记 + 文件名 / 尺寸·大小·时间 两行，右侧 信息·旋转 | 最小化·最大化·关闭
    ///   内容区：图片卡片（圆角+描边+投影），左右各一个大圆形翻页按钮
    ///   底部：悬浮工具条 [缩小][比例][放大] │ [适应][1:1][缩略图] │ [更多][删除]
    ///   最下：缩略图条（直接排在页面底色上）
    /// </summary>
    internal sealed partial class ViewerForm : Form
    {
        private const int HtResize = 6;

        private int _titleHeight = 62;
        private Rectangle _btnInfo;
        private Rectangle _btnRotate;
        private Rectangle _btnMin;
        private Rectangle _btnMax;
        private Rectangle _btnClose;

        private Rectangle _navPrev;
        private Rectangle _navNext;
        private int _hotTitleButton = -1;
        private int _pressedTitleButton = -1;
        private int _hotNav = -1;


        private readonly ImageCanvas _canvas = new ImageCanvas();
        private EditRecipe _recipe = new EditRecipe();
        private readonly Toolbar _toolbar = new Toolbar();
        private readonly FolderSource _folder = new FolderSource();
        private readonly Filmstrip _filmstrip = new Filmstrip();
        private ThumbnailCache _thumbnails;
        private int _documentIndex = -1;

        private ImageDocument _document;
        private string _documentPath;
        private string _errorText;
        private bool _fullscreen;
        private Rectangle _restoreBoundsBeforeFullscreen;
        private string _toast;
        private long _toastUntil;

        private bool _dragging;
        private Point _dragOrigin;
        private GifAnimation _animation;

        internal ViewerForm(string[] files)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            Text = string.Empty;
            KeyPreview = true;
            AllowDrop = true;
            // 任务栏 / Alt+Tab 用 exe 内嵌的同一个图标；取不到就算了，不影响使用
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }

            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.P.Backdrop;

            UpdateDpiFromHandle();
            MinimumSize = new Size(Ui.S(640), Ui.S(460));
            ApplyDefaultBounds();
            UpdateLayout();

            _canvas.NeedsRepaint += OnCanvasNeedsRepaint;

            _thumbnails = new ThumbnailCache(SynchronizationContext.Current);
            _thumbnails.ThumbnailReady += OnThumbnailReady;
            _filmstrip.Attach(_folder, _thumbnails);
            _filmstrip.Visible = Settings.Current.Filmstrip;
            ApplySavedBounds();

            string first = null;
            if (files != null && files.Length > 0)
            {
                first = files[0];
            }
            if (!string.IsNullOrEmpty(first))
            {
                _documentPath = first;
            }

            Theme.Changed += OnThemeChanged;
            Theme.Refresh();
        }

        // ---------------- 基础 ----------------

        internal ImageCanvas Canvas
        {
            get { return _canvas; }
        }

        internal FolderSource Folder
        {
            get { return _folder; }
        }

        internal ImageDocument Document
        {
            get { return _document; }
        }

        internal string DocumentPath
        {
            get { return _documentPath; }
        }

        internal int DocumentIndex
        {
            get { return _documentIndex; }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            ApplySystemChrome();
            BackColor = Theme.P.Backdrop;
            Invalidate();
        }

        private void OnCanvasNeedsRepaint(object sender, EventArgs e)
        {
            Invalidate(GetImageArea());
        }

        private void OnThumbnailReady(string path)
        {
            if (_filmstrip.Visible)
            {
                Invalidate(GetFilmstripRect());
            }
        }

        private void UpdateDpiFromHandle()
        {
            try
            {
                if (IsHandleCreated)
                {
                    Ui.SetDpi(Native.GetDpiForWindow(Handle));
                }
                else
                {
                    using (Graphics g = CreateGraphics())
                    {
                        Ui.SetDpi((uint)Math.Round(g.DpiX));
                    }
                }
            }
            catch (Exception)
            {
                Ui.Scale = 1f;
            }
        }

        internal void UpdateLayout()
        {
            _titleHeight = Ui.S(62);

            int size = Ui.S(38);
            int gap = Ui.S(4);
            int margin = Ui.S(10);
            int y = (_titleHeight - size) / 2;
            _btnClose = new Rectangle(ClientSize.Width - margin - size, y, size, size);
            _btnMax = new Rectangle(_btnClose.X - gap - size, y, size, size);
            _btnMin = new Rectangle(_btnMax.X - gap - size, y, size, size);
            _btnRotate = new Rectangle(_btnMin.X - Ui.S(12) - size, y, size, size);
            _btnInfo = new Rectangle(_btnRotate.X - gap - size, y, size, size);

            Rectangle viewport = GetViewport();
            int nav = Ui.S(40);
            int navY = viewport.Y + (viewport.Height - nav) / 2;
            _navPrev = new Rectangle(Ui.S(20), navY, nav, nav);
            _navNext = new Rectangle(ClientSize.Width - Ui.S(20) - nav, navY, nav, nav);

            _filmstrip.Layout(GetFilmstripRect());
            _toolbar.Layout(GetImageArea());
            if (_overlay != null)
            {
                _overlay.Layout(this, ClientRectangle);
            }
        }

        private void ApplyDefaultBounds()
        {
            Rectangle wa = SystemInformation.WorkingArea;
            int w = Math.Max(Ui.S(640), Math.Min(Ui.S(1180), (int)(wa.Width * 0.92)));
            int h = Math.Max(Ui.S(460), Math.Min(Ui.S(820), (int)(wa.Height * 0.92)));
            Size = new Size(w, h);
            Location = new Point(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);
        }

        private void ApplySavedBounds()
        {
            Settings settings = Settings.Current;
            if (settings.HasWindowBounds)
            {
                Rectangle saved = settings.WindowBounds;
                bool visible = false;
                foreach (Screen screen in Screen.AllScreens)
                {
                    Rectangle probe = new Rectangle(saved.X + Ui.S(60), saved.Y,
                        Math.Max(Ui.S(40), saved.Width - Ui.S(120)), saved.Height);
                    if (screen.WorkingArea.IntersectsWith(probe))
                    {
                        visible = true;
                        break;
                    }
                }
                if (visible && saved.Width >= Ui.S(480) && saved.Height >= Ui.S(360))
                {
                    Bounds = saved;
                }
            }
            if (settings.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        internal void ClampIntoWorkArea()
        {
            if (WindowState == FormWindowState.Maximized || _fullscreen)
            {
                return;
            }
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            Rectangle b = Bounds;
            int margin = Ui.S(8);
            b.Width = Math.Min(b.Width, Math.Max(Ui.S(480), wa.Width - margin * 2));
            b.Height = Math.Min(b.Height, Math.Max(Ui.S(360), wa.Height - margin * 2));
            if (b.X < wa.X + margin) { b.X = wa.X + margin; }
            if (b.Y < wa.Y + margin) { b.Y = wa.Y + margin; }
            if (b.Right > wa.Right - margin) { b.X = wa.Right - margin - b.Width; }
            if (b.Bottom > wa.Bottom - margin) { b.Y = wa.Bottom - margin - b.Height; }
            if (Bounds != b)
            {
                Bounds = b;
            }
        }

        internal void ApplyFilmstripSetting()
        {
            _filmstrip.Visible = Settings.Current.Filmstrip;
            UpdateLayout();
            UpdateToolbarState();
            Invalidate();
        }

        private void SaveSettings()
        {
            try
            {
                Settings settings = Settings.Current;
                settings.Filmstrip = _filmstrip.Visible;
                settings.Maximized = WindowState == FormWindowState.Maximized;
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (bounds.Width > Ui.S(200) && bounds.Height > Ui.S(150))
                {
                    settings.WindowBounds = bounds;
                    settings.HasWindowBounds = true;
                }
                settings.Save();
            }
            catch (Exception ex)
            {
                AppLog.Error("SaveSettings", ex);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplySystemChrome();
        }

        private void ApplySystemChrome()
        {
            if (!IsHandleCreated)
            {
                return;
            }
            try
            {
                Native.SetDarkTitleBar(Handle, Theme.IsDark);
                Native.TryRoundCorners(Handle);
            }
            catch (Exception)
            {
            }
        }

        // ---------------- 生命周期 ----------------

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateDpiFromHandle();
            UpdateLayout();
            ClampIntoWorkArea();
            UpdateLayout();
            if (!string.IsNullOrEmpty(_documentPath))
            {
                OpenPath(_documentPath, true);
            }
            ApplyDemoMode();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayout();
            Invalidate();
            SyncAnimationPlayState();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Ui.SetDpi((uint)e.DeviceDpiNew);
            MinimumSize = new Size(Ui.S(640), Ui.S(460));
            UpdateLayout();
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveSettings();
            StopAnimation();
            Theme.Changed -= OnThemeChanged;
            _canvas.NeedsRepaint -= OnCanvasNeedsRepaint;
            _canvas.Release();
            if (_thumbnails != null)
            {
                _thumbnails.ThumbnailReady -= OnThumbnailReady;
                _thumbnails.Dispose();
                _thumbnails = null;
            }
            if (_document != null)
            {
                _document.Dispose();
                _document = null;
            }
            GdiBlitter.ReleaseSource();
            base.OnFormClosed(e);
        }

        private void ApplyDemoMode()
        {
            string demo = Program.DemoMode;
            if (string.IsNullOrEmpty(demo))
            {
                return;
            }
            switch (demo.ToLowerInvariant())
            {
                case "menu":
                    ShowContextMenu(new Point(_toolbar.Bounds.X + Ui.S(24), _toolbar.Bounds.Bottom));
                    break;
                case "resize":
                    ShowResizeOverlay();
                    break;
                case "settings":
                    ShowSettingsOverlay();
                    break;
                case "info":
                    ShowInfoOverlay();
                    break;
                case "crop":
                    ShowCropOverlay();
                    break;
                case "confirm":
                    RequestDelete();
                    break;
                case "batch":
                    ShowBatchOverlay();
                    break;
                case "adjust":
                    ShowAdjustOverlay();
                    _recipe.Brightness = 18f;
                    _recipe.Contrast = 12f;
                    _recipe.Saturation = 8f;
                    OnRecipeChanged(false);
                    break;
            }
        }

        // ---------------- 区域 ----------------

        internal Rectangle GetImageArea()
        {
            int top = _fullscreen ? 0 : _titleHeight;
            int bottom = ClientSize.Height - (_fullscreen ? 0 : GetFilmstripHeight());
            return new Rectangle(0, top, ClientSize.Width, Math.Max(0, bottom - top));
        }

        /// <summary>图片真正绘制/居中的区域：内容区上下留白，底部为工具条让出位置。</summary>
        internal Rectangle GetViewport()
        {
            Rectangle area = GetImageArea();
            if (_fullscreen)
            {
                return area;
            }
            int top = area.Y + Ui.S(10);
            int bottom = area.Bottom - Ui.S(66);
            if (bottom - top < Ui.S(80))
            {
                bottom = area.Bottom - Ui.S(8);
            }
            return new Rectangle(area.X, top, area.Width, Math.Max(0, bottom - top));
        }

        private int GetFilmstripHeight()
        {
            return _filmstrip.Visible ? Filmstrip.Height : 0;
        }

        private Rectangle GetFilmstripRect()
        {
            int h = GetFilmstripHeight();
            return new Rectangle(0, ClientSize.Height - h, ClientSize.Width, h);
        }

        // ---------------- 绘制 ----------------

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (Profile)
            {
                Stopwatch phase = Stopwatch.StartNew();
                PaintBackdrop(g);
                _msBackdrop += phase.ElapsedTicks;
                phase.Restart();
                PaintImageArea(g);
                _msImage += phase.ElapsedTicks;
                phase.Restart();
                if (!_fullscreen)
                {
                    PaintTitleBar(g);
                    PaintWindowBorder(g);
                }
                _msChrome += phase.ElapsedTicks;
                phase.Restart();
                _toolbar.Paint(g);
                PaintToast(g);
                _msToolbar += phase.ElapsedTicks;
                phase.Stop();
                _paintSamples++;
                return;
            }

            PaintBackdrop(g);
            PaintImageArea(g);
            if (!_fullscreen)
            {
                PaintNavArrow(g, _navPrev, IconId.ChevronLeft, 0);
                PaintNavArrow(g, _navNext, IconId.ChevronRight, 1);
                _filmstrip.Paint(g);
                PaintTitleBar(g);
            }
            _toolbar.Paint(g);
            if (_overlay != null)
            {
                _overlay.Paint(g, this);
            }
            PaintToast(g);
            PaintWindowBorder(g);
        }

        internal static bool Profile;
        private static long _msBackdrop;
        private static long _msImage;
        private static long _msChrome;
        private static long _msToolbar;
        private static int _paintSamples;

        internal static string ProfileReport()
        {
            if (_paintSamples == 0)
            {
                return "(no samples)";
            }
            double tick = 1000.0 / Stopwatch.Frequency / _paintSamples;
            return string.Format("绘制分阶段 背景={0:0.00}ms 图片={1:0.00}ms 外框={2:0.00}ms 工具栏={3:0.00}ms (样本 {4})",
                _msBackdrop * tick, _msImage * tick, _msChrome * tick, _msToolbar * tick, _paintSamples);
        }

        private void PaintBackdrop(Graphics g)
        {
            Palette p = Theme.P;
            g.Clear(p.Backdrop);
            if (_fullscreen)
            {
                return;
            }
            // 标题栏与页面同色（参考图是很轻的浅色），只留一条极淡的分隔线
            g.FillRectangle(PaintKit.Brush(p.Backdrop), new Rectangle(0, 0, ClientSize.Width, _titleHeight));
        }

        private void PaintWindowBorder(Graphics g)
        {
            if (WindowState == FormWindowState.Maximized || _fullscreen)
            {
                return;
            }
            Palette p = Theme.P;
            g.DrawRectangle(PaintKit.PenFor(Color.FromArgb(p.Dark ? 120 : 200, p.BorderStrong), 1f), 0, 0,
                ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private void PaintTitleBar(Graphics g)
        {
            Palette p = Theme.P;

            int glyph = Ui.S(34);
            Rectangle glyphRect = new Rectangle(Ui.S(18), (_titleHeight - glyph) / 2, glyph, glyph);
            PaintKit.AppGlyph(g, glyphRect, p.Accent, p.Surface);

            int textLeft = glyphRect.Right + Ui.S(14);
            int textRight = _btnInfo.X - Ui.S(16);

            string name = string.IsNullOrEmpty(_documentPath) ? Strings.AppName : Path.GetFileName(_documentPath);
            Rectangle nameRect = new Rectangle(textLeft, Ui.S(11), Math.Max(0, textRight - textLeft), Ui.S(24));
            PaintKit.Text(g, name, Fonts.Title, p.Text, nameRect, StringAlignment.Near, StringAlignment.Center, true);

            string meta = BuildMetaLine();
            if (!string.IsNullOrEmpty(meta))
            {
                Rectangle metaRect = new Rectangle(textLeft, Ui.S(33), Math.Max(0, textRight - textLeft), Ui.S(20));
                PaintKit.Text(g, meta, Fonts.Small, p.TextDim, metaRect, StringAlignment.Near,
                    StringAlignment.Center, true);
            }

            PaintTitleButton(g, _btnInfo, IconId.Info, 0, Strings.InfoTitle);
            PaintTitleButton(g, _btnRotate, IconId.Rotate, 1, Strings.TipRotate);
            PaintTitleButton(g, _btnMin, IconId.Minimize, 2, Strings.TipMinimize);
            PaintTitleButton(g, _btnMax, WindowState == FormWindowState.Maximized ? IconId.Restore : IconId.Maximize,
                3, WindowState == FormWindowState.Maximized ? Strings.TipRestore : Strings.TipMaximize);
            PaintTitleButton(g, _btnClose, IconId.Close, 4, Strings.TipClose);
        }

        private void PaintTitleButton(Graphics g, Rectangle rect, IconId icon, int index, string tip)
        {
            Palette p = Theme.P;
            bool hot = _hotTitleButton == index;
            bool pressed = _pressedTitleButton == index;
            bool isClose = index == 4;

            Color fill = hot
                ? (isClose ? p.Danger : p.SurfaceAlt)
                : (p.Dark ? Color.FromArgb(0x22, 0x22, 0x26) : Color.FromArgb(0xF3, 0xF3, 0xF6));
            if (pressed)
            {
                fill = isClose ? Color.FromArgb(210, p.Danger) : p.Border;
            }
            PaintKit.FillRound(g, rect, Ui.SF(9f), fill);

            Color ink = isClose && (hot || pressed) ? Color.White : p.Text;
            int s = Ui.S(15);
            PaintKit.Icon(g, icon, new Rectangle(rect.X + (rect.Width - s) / 2, rect.Y + (rect.Height - s) / 2, s, s),
                ink, Math.Max(1f, Ui.SF(1.35f)));
        }

        private void PaintNavArrow(Graphics g, Rectangle rect, IconId icon, int index)
        {
            if (!NavVisible)
            {
                return;
            }
            Palette p = Theme.P;
            bool hot = _hotNav == index;

            PaintKit.Shadow(g, rect, rect.Height / 2f, Ui.S(6), p.Dark ? 110 : 34);
            PaintKit.FillRound(g, rect, rect.Height / 2f, hot ? p.Surface : Color.FromArgb(242, p.Surface));
            PaintKit.StrokeRound(g, rect, rect.Height / 2f, Color.FromArgb(p.Dark ? 80 : 130, p.Border), 1f);

            int s = Ui.S(16);
            PaintKit.Icon(g, icon, new Rectangle(rect.X + (rect.Width - s) / 2, rect.Y + (rect.Height - s) / 2, s, s),
                p.Text, Math.Max(1f, Ui.SF(1.5f)));
        }

        /// <summary>
        /// 翻页按钮是否可见。注意这里**不能**要求文档有效：
        /// 文件损坏/格式不支持时正是最需要跳到下一张的时候。
        /// </summary>
        private bool NavVisible
        {
            get { return !_fullscreen && _folder.Count > 1 && !string.IsNullOrEmpty(_documentPath); }
        }


        private void PaintImageArea(Graphics g)
        {
            Rectangle area = GetImageArea();
            if (area.Width <= 0 || area.Height <= 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(_documentPath))
            {
                PaintEmptyState(g, GetViewport());
                return;
            }
            if (_document == null || !_document.IsValid)
            {
                PaintErrorState(g, GetViewport());
                return;
            }

            _canvas.Paint(g, GetViewport());
        }

        private void PaintEmptyState(Graphics g, Rectangle area)
        {
            Palette p = Theme.P;
            int iconSize = Ui.S(72);
            Rectangle iconRect = new Rectangle(area.X + (area.Width - iconSize) / 2,
                area.Y + area.Height / 2 - Ui.S(76), iconSize, iconSize);
            // 品牌位：画应用图标本身，让空状态和 exe/任务栏/标题栏是的同一个标记
            PaintKit.AppGlyph(g, iconRect, p.Accent, p.Surface);

            Rectangle titleRect = new Rectangle(area.X, iconRect.Bottom + Ui.S(16), area.Width, Ui.S(30));
            PaintKit.Text(g, Strings.EmptyTitle, Fonts.Huge, p.Text, titleRect, StringAlignment.Center,
                StringAlignment.Center, false);

            Rectangle hintRect = new Rectangle(area.X, titleRect.Bottom + Ui.S(2), area.Width, Ui.S(24));
            PaintKit.Text(g, Strings.EmptyHint, Fonts.Body, p.TextDim, hintRect, StringAlignment.Center,
                StringAlignment.Center, false);
        }

        private void PaintErrorState(Graphics g, Rectangle area)
        {
            Palette p = Theme.P;
            int iconSize = Ui.S(44);
            Rectangle iconRect = new Rectangle(area.X + (area.Width - iconSize) / 2,
                area.Y + area.Height / 2 - Ui.S(54), iconSize, iconSize);
            PaintKit.Icon(g, IconId.Warning, iconRect, p.TextDim, Math.Max(1f, Ui.SF(1.2f)));

            Rectangle titleRect = new Rectangle(area.X, iconRect.Bottom + Ui.S(14), area.Width, Ui.S(26));
            PaintKit.Text(g, Strings.DecodeFailedTitle, Fonts.Large, p.Text, titleRect, StringAlignment.Center,
                StringAlignment.Center, false);

            Rectangle hintRect = new Rectangle(area.X, titleRect.Bottom + Ui.S(2), area.Width, Ui.S(22));
            string hint = string.IsNullOrEmpty(_errorText) ? Strings.DecodeFailedHint : _errorText;
            PaintKit.Text(g, hint, Fonts.Body, p.TextDim, hintRect, StringAlignment.Center,
                StringAlignment.Center, false);

        }

        private void PaintToast(Graphics g)
        {
            if (_toast == null || Clock.NowMs > _toastUntil)
            {
                return;
            }
            Palette p = Theme.P;
            SizeF size = PaintKit.Measure(g, _toast, Fonts.Body);
            int padX = Ui.S(16);
            int padY = Ui.S(10);
            int w = (int)size.Width + padX * 2;
            int h = (int)size.Height + padY * 2;
            Rectangle area = GetImageArea();
            Rectangle box = new Rectangle(area.X + (area.Width - w) / 2, area.Y + Ui.S(14), w, h);
            PaintKit.Shadow(g, box, Ui.SF(10f), Ui.S(5), p.Dark ? 120 : 40);
            PaintKit.FillRound(g, box, Ui.SF(10f), p.Surface);
            PaintKit.StrokeRound(g, box, Ui.SF(10f), Color.FromArgb(140, p.Border), 1f);
            PaintKit.Text(g, _toast, Fonts.Body, p.Text, box, StringAlignment.Center, StringAlignment.Center, false);
        }

        internal void ShowToast(string message)
        {
            _toast = message;
            _toastUntil = Clock.NowMs + 2200;
            Invalidate();
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 2300;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                Invalidate();
            };
            timer.Start();
        }

        // ---------------- 消息 ----------------

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_NCCALCSIZE:
                    if (m.WParam != IntPtr.Zero)
                    {
                        Native.NCCALCSIZE_PARAMS prm = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(
                            m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                        if (WindowState == FormWindowState.Maximized)
                        {
                            int fx = Native.GetSystemMetrics(32) + Native.GetSystemMetrics(92);
                            int fy = Native.GetSystemMetrics(33) + Native.GetSystemMetrics(92);
                            prm.Rc0.Left += fx;
                            prm.Rc0.Top += fy;
                            prm.Rc0.Right -= fx;
                            prm.Rc0.Bottom -= fy;
                        }
                        Marshal.StructureToPtr(prm, m.LParam, false);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case Native.WM_NCHITTEST:
                    m.Result = (IntPtr)HitTest(m.LParam);
                    return;

                case Native.WM_GETMINMAXINFO:
                    {
                        Native.MINMAXINFO info = (Native.MINMAXINFO)Marshal.PtrToStructure(m.LParam,
                            typeof(Native.MINMAXINFO));
                        info.MinTrackSize.X = Ui.S(640);
                        info.MinTrackSize.Y = Ui.S(460);
                        Marshal.StructureToPtr(info, m.LParam, false);
                        m.Result = IntPtr.Zero;
                        return;
                    }

                case Native.WM_SETTINGCHANGE:
                    base.WndProc(ref m);
                    Theme.Refresh();
                    return;

                case Native.WM_ERASEBKGND:
                    m.Result = (IntPtr)1;
                    return;

                case 0x00A0: // WM_NCMOUSEMOVE
                    base.WndProc(ref m);
                    if (_hotTitleButton != -1)
                    {
                        _hotTitleButton = -1;
                        Invalidate(new Rectangle(0, 0, ClientSize.Width, _titleHeight));
                    }
                    return;
            }

            base.WndProc(ref m);
        }

        private int HitTest(IntPtr lParam)
        {
            int screenX = unchecked((short)((long)lParam & 0xFFFF));
            int screenY = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
            Point pt = PointToClient(new Point(screenX, screenY));

            bool maximized = WindowState == FormWindowState.Maximized;
            if (!maximized && !_fullscreen)
            {
                int b = Ui.S(HtResize);
                bool left = pt.X >= 0 && pt.X < b;
                bool right = pt.X <= ClientSize.Width - 1 && pt.X >= ClientSize.Width - b;
                bool top = pt.Y >= 0 && pt.Y < b;
                bool bottom = pt.Y <= ClientSize.Height - 1 && pt.Y >= ClientSize.Height - b;

                if (top && left) { return Native.HTTOPLEFT; }
                if (top && right) { return Native.HTTOPRIGHT; }
                if (bottom && left) { return Native.HTBOTTOMLEFT; }
                if (bottom && right) { return Native.HTBOTTOMRIGHT; }
                if (left) { return Native.HTLEFT; }
                if (right) { return Native.HTRIGHT; }
                if (top) { return Native.HTTOP; }
                if (bottom) { return Native.HTBOTTOM; }
            }

            if (!_fullscreen && TitleButtonAt(pt) != -1)
            {
                return Native.HTCLIENT;
            }

            if (!_fullscreen && pt.Y >= 0 && pt.Y < _titleHeight)
            {
                return Native.HTCAPTION;
            }

            return Native.HTCLIENT;
        }

        private int TitleButtonAt(Point point)
        {
            if (_btnInfo.Contains(point)) { return 0; }
            if (_btnRotate.Contains(point)) { return 1; }
            if (_btnMin.Contains(point)) { return 2; }
            if (_btnMax.Contains(point)) { return 3; }
            if (_btnClose.Contains(point)) { return 4; }
            return -1;
        }

        // ---------------- 鼠标 ----------------

        private Point ToViewport(Point client)
        {
            Rectangle viewport = GetViewport();
            return new Point(client.X - viewport.X, client.Y - viewport.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_overlay != null && _overlay.HandleMouseMove(e.Location, this))
            {
                Invalidate();
                return;
            }

            int hot = TitleButtonAt(e.Location);
            if (hot != _hotTitleButton)
            {
                _hotTitleButton = hot;
                Invalidate(new Rectangle(0, 0, ClientSize.Width, _titleHeight));
            }

            int hotNav = -1;
            if (NavVisible)
            {
                if (_navPrev.Contains(e.Location)) { hotNav = 0; }
                else if (_navNext.Contains(e.Location)) { hotNav = 1; }
            }
            if (hotNav != _hotNav)
            {
                _hotNav = hotNav;
                Invalidate(GetImageArea());
            }

            if (_toolbar.Visible)
            {
                int toolbarIndex;
                _toolbar.HitTest(e.Location, out toolbarIndex);
                if (toolbarIndex != _toolbar.HotIndex)
                {
                    _toolbar.HotIndex = toolbarIndex;
                    Invalidate(_toolbar.Bounds);
                }
            }


            if (_dragging)
            {
                int dx = e.X - _dragOrigin.X;
                int dy = e.Y - _dragOrigin.Y;
                if (dx != 0 || dy != 0)
                {
                    _dragOrigin = e.Location;
                    _canvas.PanBy(dx, dy);
                    Invalidate(GetImageArea());
                }
                return;
            }

            UpdateCursor(e.Location);
        }

        private void UpdateCursor(Point point)
        {
            Rectangle viewport = GetViewport();
            bool overImage = viewport.Contains(point) && _document != null && _document.IsValid;
            bool overToolbar = _toolbar.ContainsPoint(point);
            bool overStrip = _filmstrip.Visible && _filmstrip.Bounds.Contains(point);
            bool overNav = NavVisible && (_navPrev.Contains(point) || _navNext.Contains(point));

            if (_dragging)
            {
                Cursor = Cursors.SizeAll;
            }
            else if (overToolbar || overStrip || overNav || TitleButtonAt(point) >= 0)
            {
                Cursor = Cursors.Hand;
            }
            else if (overImage && _canvas.CanPan)
            {
                Cursor = Cursors.SizeAll;
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotTitleButton != -1 || _toolbar.HotIndex != -1 || _hotNav != -1)
            {
                _hotTitleButton = -1;
                _toolbar.HotIndex = -1;
                _hotNav = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (_overlay != null && _overlay.HandleMouseDown(e.Location, this))
            {
                Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int pressed = TitleButtonAt(e.Location);
            if (pressed != -1)
            {
                _pressedTitleButton = pressed;
                Invalidate(new Rectangle(0, 0, ClientSize.Width, _titleHeight));
                return;
            }


            if (NavVisible)
            {
                if (_navPrev.Contains(e.Location))
                {
                    NavigateRelative(-1);
                    return;
                }
                if (_navNext.Contains(e.Location))
                {
                    NavigateRelative(1);
                    return;
                }
            }

            if (_toolbar.Visible)
            {
                int index;
                Toolbar.Command command = _toolbar.HitTest(e.Location, out index);
                if (command != Toolbar.Command.None)
                {
                    _toolbar.PressedIndex = index;
                    Invalidate(_toolbar.Bounds);
                    return;
                }
            }

            int stripIndex = _filmstrip.HitTest(e.Location);
            if (stripIndex >= 0)
            {
                NavigateTo(stripIndex);
                return;
            }

            Rectangle viewport = GetViewport();
            if (viewport.Contains(e.Location) && _canvas.CanPan)
            {
                _dragging = true;
                _dragOrigin = e.Location;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_overlay != null && _overlay.HandleMouseUp(e.Location, this))
            {
                Invalidate();
                return;
            }

            int pressed = _pressedTitleButton;
            _pressedTitleButton = -1;
            if (pressed != -1)
            {
                Invalidate(new Rectangle(0, 0, ClientSize.Width, _titleHeight));
                if (TitleButtonAt(e.Location) != pressed)
                {
                    return;
                }
                switch (pressed)
                {
                    case 0:
                        ShowInfoOverlay();
                        break;
                    case 1:
                        _canvas.RotateClockwise();
                        UpdateToolbarState();
                        break;
                    case 2:
                        WindowState = FormWindowState.Minimized;
                        break;
                    case 3:
                        WindowState = WindowState == FormWindowState.Maximized
                            ? FormWindowState.Normal
                            : FormWindowState.Maximized;
                        break;
                    case 4:
                        Close();
                        return;
                }
                Invalidate();
                return;
            }

            int toolbarPressed = _toolbar.PressedIndex;
            _toolbar.PressedIndex = -1;
            if (toolbarPressed != -1)
            {
                int index;
                Toolbar.Command command = _toolbar.HitTest(e.Location, out index);
                if (command != Toolbar.Command.None && index == toolbarPressed)
                {
                    ExecuteToolbar(command);
                }
                Invalidate(_toolbar.Bounds);
                return;
            }

            if (_dragging)
            {
                _dragging = false;
                UpdateCursor(e.Location);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_overlay != null || e.Button != MouseButtons.Left)
            {
                return;
            }

            // 左右翻页圆和工具条都压在图片区里，点它们不能被当成“双击图片”。
            // 之前的 bug：连点几下翻页，系统把任意两次快速点击都算双击 → 突然全屏。
            if (NavVisible && (_navPrev.Contains(e.Location) || _navNext.Contains(e.Location)))
            {
                return;
            }
            if (_toolbar.ContainsPoint(e.Location))
            {
                return;
            }
            if (_filmstrip.Visible && _filmstrip.Bounds.Contains(e.Location))
            {
                return;
            }

            if (!GetViewport().Contains(e.Location))
            {
                return;
            }
            SetFullscreen(!_fullscreen);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_overlay != null)
            {
                if (_overlay.HandleWheel(e.Delta, e.Location, this))
                {
                    Invalidate();
                    return;
                }
                if (_overlay.BlocksInput)
                {
                    return;
                }
            }
            if (_filmstrip.Visible && _filmstrip.Bounds.Contains(e.Location))
            {
                _filmstrip.ScrollBy(-e.Delta);
                Invalidate(_filmstrip.Bounds);
                return;
            }
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            Rectangle viewport = GetViewport();
            if (!viewport.Contains(e.Location) || _toolbar.ContainsPoint(e.Location))
            {
                return;
            }
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            _canvas.ZoomAt(ToViewport(e.Location), factor);
            UpdateToolbarState();
            Invalidate(GetImageArea());
        }

        private void ExecuteToolbar(Toolbar.Command command)
        {
            switch (command)
            {
                case Toolbar.Command.ZoomOut:
                    _canvas.ZoomBy(1.0 / 1.25);
                    break;
                case Toolbar.Command.ZoomIn:
                    _canvas.ZoomBy(1.25);
                    break;
                case Toolbar.Command.Fit:
                    _canvas.ZoomToFit();
                    break;
                case Toolbar.Command.Actual:
                    _canvas.ZoomToActual();
                    break;
                case Toolbar.Command.Filmstrip:
                    ToggleFilmstrip();
                    break;
                case Toolbar.Command.Menu:
                    ShowContextMenu(new Point(_toolbar.Bounds.X + Ui.S(24), _toolbar.Bounds.Y - Ui.S(6)));
                    break;
                case Toolbar.Command.Delete:
                    RequestDelete();
                    break;
            }
            UpdateToolbarState();
            Invalidate();
        }

        private void ToggleFilmstrip()
        {
            _filmstrip.Visible = !_filmstrip.Visible;
            Settings.Current.Filmstrip = _filmstrip.Visible;
            Settings.Current.Save();
            UpdateLayout();
            UpdateToolbarState();
            Invalidate();
        }

        internal void UpdateToolbarState()
        {
            _toolbar.Scale = _canvas.Scale;
            _toolbar.FitActive = _canvas.IsFitMode;
            _toolbar.ActualActive = !_canvas.IsFitMode && Math.Abs(_canvas.Scale - 1.0) < 0.01;
            _toolbar.FilmstripActive = _filmstrip.Visible;
            _toolbar.Visible = _document != null && _document.IsValid && !_fullscreen;
            _toolbar.Layout(GetImageArea());
        }

        // ---------------- 键盘 ----------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_overlay != null && _overlay.HandleKey(keyData, this))
            {
                Invalidate();
                return true;
            }

            if ((keyData & Keys.Control) == Keys.Control)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.O:
                        ShowOpenDialog();
                        return true;
                    case Keys.R:
                        ShowResizeOverlay();
                        return true;
                    case Keys.C:
                        CopyImageToClipboard();
                        return true;
                    case Keys.K:
                        ShowCropOverlay();
                        return true;
                    case Keys.E:
                        ShowAdjustOverlay();
                        return true;
                    case Keys.S:
                        SaveEditedCopy();
                        return true;
                    case Keys.B:
                        ShowBatchOverlay();
                        return true;

                    case Keys.Oemcomma:
                        ShowSettingsOverlay();
                        return true;
                    case Keys.T:
                        CycleTheme();
                        return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    if (_overlay != null)
                    {
                        _overlay = null;
                        Invalidate();
                        e.Handled = true;
                    }
                    else if (_fullscreen)
                    {
                        SetFullscreen(false);
                        e.Handled = true;
                    }
                    break;
                case Keys.F11:
                    SetFullscreen(!_fullscreen);
                    e.Handled = true;
                    break;
                case Keys.Tab:
                    ToggleFilmstrip();
                    e.Handled = true;
                    break;
                case Keys.D0:
                    if ((e.Modifiers & Keys.Control) == 0)
                    {
                        _canvas.ZoomToFit();
                        e.Handled = true;
                    }
                    break;
                case Keys.D1:
                    _canvas.ZoomToActual();
                    e.Handled = true;
                    break;
                case Keys.R:
                    _canvas.RotateClockwise();
                    e.Handled = true;
                    break;
                case Keys.Oemplus:
                case Keys.Add:
                    _canvas.ZoomBy(1.25);
                    e.Handled = true;
                    break;
                case Keys.OemMinus:
                case Keys.Subtract:
                    _canvas.ZoomBy(1.0 / 1.25);
                    e.Handled = true;
                    break;
                case Keys.Left:
                case Keys.PageUp:
                    NavigateRelative(-1);
                    e.Handled = true;
                    break;
                case Keys.Right:
                case Keys.PageDown:
                    NavigateRelative(1);
                    e.Handled = true;
                    break;
                case Keys.Home:
                    if (_folder.Count > 0)
                    {
                        NavigateTo(0);
                    }
                    e.Handled = true;
                    break;
                case Keys.End:
                    if (_folder.Count > 0)
                    {
                        NavigateTo(_folder.Count - 1);
                    }
                    e.Handled = true;
                    break;
                case Keys.Delete:
                    RequestDelete();
                    e.Handled = true;
                    break;
            }
            if (e.Handled)
            {
                UpdateToolbarState();
                Invalidate();
            }
        }

        private void SetFullscreen(bool on)
        {
            if (_fullscreen == on)
            {
                return;
            }
            if (on)
            {
                _restoreBoundsBeforeFullscreen = Bounds;
                _fullscreen = true;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                _fullscreen = false;
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = _restoreBoundsBeforeFullscreen;
            }
            _hotTitleButton = -1;
            _hotNav = -1;
            UpdateLayout();
            UpdateToolbarState();
            Invalidate();
        }

        internal void ToggleFullscreen()
        {
            SetFullscreen(!_fullscreen);
        }

        internal void CycleTheme()
        {
            Theme.Mode = Theme.NextMode();
            ShowToast("外观：" + Theme.Label(Theme.Mode));
        }

        // ---------------- 打开与导航 ----------------

        internal void OpenPath(string path, bool firstOpen)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (_folder.LoadFor(path) && _thumbnails != null)
            {
                _thumbnails.Reset();
            }

            if (!File.Exists(path))
            {
                _documentPath = path;
                _errorText = Strings.FileMissing;
                ReplaceDocument(null);
                _documentIndex = -1;
                UpdateToolbarState();
                Invalidate();
                return;
            }

            ImageDocument previous = _document;
            ImageDocument document = ImageDocument.Open(path);

            _documentPath = path;
            Text = Path.GetFileName(path);
            ReplaceDocument(document);

            _documentIndex = _folder.IndexOf(path);
            _filmstrip.Selected = _documentIndex;
            if (_documentIndex >= 0)
            {
                _filmstrip.EnsureVisible(_documentIndex);
            }

            _errorText = document.IsValid ? null : document.Error;

            if (previous != null)
            {
                previous.Dispose();
            }

            StartAnimation(document);
            UpdateToolbarState();
            UpdateLayout();
            Invalidate();
        }

        /// <summary>标题栏第二行：位置 · 尺寸 · 体积 · 修改时间。</summary>
        private string BuildMetaLine()
        {
            if (_document == null || !_document.IsValid)
            {
                return string.IsNullOrEmpty(_errorText) ? null : _errorText;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (_documentIndex >= 0 && _folder.Count > 0)
            {
                sb.Append(_documentIndex + 1).Append(" / ").Append(_folder.Count).Append("   ·   ");
            }
            EditRecipe recipe = _recipe;
            int editWidth = _document.PixelWidth;
            int editHeight = _document.PixelHeight;
            if (recipe != null && recipe.HasCrop)
            {
                Rectangle crop = recipe.CropPixels(_document.PixelWidth, _document.PixelHeight);
                editWidth = crop.Width;
                editHeight = crop.Height;
            }
            sb.Append(editWidth).Append(" × ").Append(editHeight);
            sb.Append("   ·   ").Append(FormatSize(_document.FileSize));
            if (HasEdits)
            {
                sb.Append("   ·   已编辑");
            }
            if (_document.Modified.Year > 1900)
            {
                sb.Append("   ·   ").Append(_document.Modified.ToString("yyyy-MM-dd HH:mm"));
            }
            if (_document.IsAnimated)
            {
                sb.Append("   ·   动图 ").Append(_document.FrameCount).Append(" 帧");
            }
            return sb.ToString();
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

        private void ReplaceDocument(ImageDocument document)
        {
            _document = document;
            ResetRecipe();
            _canvas.SetDocument(document);
            Rectangle viewport = GetViewport();
            _canvas.SetViewport(new Rectangle(0, 0, viewport.Width, viewport.Height));
            _canvas.ZoomToFit();
            UpdateToolbarState();
        }

        internal void NavigateRelative(int delta)
        {
            if (_folder.Count == 0)
            {
                return;
            }
            int index = _documentIndex < 0 ? 0 : _documentIndex;
            int next = index + delta;
            if (next < 0)
            {
                next = _folder.Count - 1;
            }
            if (next >= _folder.Count)
            {
                next = 0;
            }
            NavigateTo(next);
        }

        internal void NavigateTo(int index)
        {
            string path = _folder.PathAt(index);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            if (_document != null)
            {
                _document.TrimCaches();
            }
            OpenPath(path, false);
        }

        private void ShowOpenDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "打开图片";
                dialog.Filter = Formats.OpenFilter;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    OpenPath(dialog.FileName, false);
                }
            }
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] paths = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null && paths.Length > 0)
            {
                OpenPath(paths[0], false);
            }
        }

        // ---------------- 动图 ----------------

        private void StartAnimation(ImageDocument document)
        {
            StopAnimation();
            if (document == null || !document.IsAnimated || !document.IsValid)
            {
                return;
            }
            GifAnimation animation = new GifAnimation(document.FilePath);
            if (!animation.Load())
            {
                animation.Dispose();
                return;
            }
            _animation = animation;
            _animation.FrameChanged += OnAnimationFrame;
            _canvas.AnimationFrame = _animation.Current;
            _animation.Start();
        }

        private void StopAnimation()
        {
            if (_animation == null)
            {
                return;
            }
            _animation.FrameChanged -= OnAnimationFrame;
            _animation.Dispose();
            _animation = null;
            _canvas.AnimationFrame = null;
        }

        private void OnAnimationFrame(object sender, EventArgs e)
        {
            // 动图是原地改写同一张位图，GDI 贴图的 HBITMAP 快照会变旧，必须每帧失效
            GdiBlitter.ReleaseSource();
            Invalidate(GetImageArea());
        }

        private void SyncAnimationPlayState()
        {
            if (_animation == null)
            {
                return;
            }
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                _animation.Start();
            }
            else
            {
                _animation.Stop();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            SyncAnimationPlayState();
        }

        // ---------------- 浮层 ----------------

        private Overlay _overlay;

        internal Overlay CurrentOverlay
        {
            get { return _overlay; }
        }

        internal void SetOverlay(Overlay overlay)
        {
            if (ReferenceEquals(_overlay, overlay))
            {
                return;
            }
            if (_overlay != null)
            {
                _overlay.Detach(this);
            }
            _overlay = overlay;
            if (overlay != null)
            {
                overlay.Layout(this, ClientRectangle);
            }
            Invalidate();
        }

        private void ShowContextMenu(Point location)
        {
            SetOverlay(new ContextMenuOverlay(location));
        }

        internal void ShowResizeOverlay()
        {
            if (_document == null || !_document.IsValid)
            {
                ShowToast("先打开一张图片");
                return;
            }
            SetOverlay(new ResizeOverlay(_document));
        }

        internal void ShowSettingsOverlay()
        {
            SetOverlay(new SettingsOverlay());
        }

        /// <summary>当前编辑参数（裁剪 + 颜色）。切换图片时重置。</summary>
        internal EditRecipe Recipe
        {
            get
            {
                if (_recipe == null)
                {
                    _recipe = new EditRecipe();
                }
                return _recipe;
            }
        }

        /// <summary>是否已经应用过裁剪或颜色调整。</summary>
        internal bool HasEdits
        {
            get { return _recipe != null && !_recipe.IsIdentity; }
        }

        /// <summary>图片在客户区里实际占据的矩形（裁剪浮层要用）。</summary>
        internal RectangleF GetCanvasDisplayRect()
        {
            Rectangle viewport = GetViewport();
            RectangleF rect = _canvas.GetDisplayRect();
            rect.Offset(viewport.X, viewport.Y);
            return rect;
        }

        internal void SetRecipe(EditRecipe recipe, bool structural)
        {
            _recipe = recipe ?? new EditRecipe();
            _canvas.Recipe = _recipe.IsIdentity ? null : _recipe;
            if (structural)
            {
                _canvas.ZoomToFit();
            }
            InvalidateImageArea();
            InvalidateTitleBar();
        }

        /// <summary>浮层改动参数后调用：structural 表示裁剪变了（要重新适配）。</summary>
        internal void OnRecipeChanged(bool structural)
        {
            SetRecipe(_recipe, structural);
        }

        private void ResetRecipe()
        {
            _recipe = new EditRecipe();
            _canvas.Recipe = null;
        }

        private void InvalidateImageArea()
        {
            Invalidate(GetImageArea());
        }

        private void InvalidateTitleBar()
        {
            Invalidate(new Rectangle(0, 0, ClientSize.Width, _titleHeight));
        }

        internal void ShowCropOverlay()
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            SetOverlay(new CropOverlay());
        }

        internal void ShowBatchOverlay()
        {
            if (_folder.Count == 0)
            {
                ShowToast("当前目录里没有图片");
                return;
            }
            System.Collections.Generic.List<string> files = new System.Collections.Generic.List<string>(_folder.Count);
            for (int i = 0; i < _folder.Count; i++)
            {
                files.Add(_folder.PathAt(i));
            }
            BatchOverlay overlay = new BatchOverlay(files);
            if (!overlay.HasItems)
            {
                ShowToast("当前目录里没有图片");
                return;
            }
            SetOverlay(overlay);
        }

        internal void ShowAdjustOverlay()
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            SetOverlay(new AdjustOverlay());
        }

        /// <summary>把当前编辑参数落到一个新文件（永不覆盖原图）。</summary>
        internal void SaveEditedCopy()
        {
            if (_document == null || !_document.IsValid)
            {
                ShowToast(Strings.ToastNothingToSave);
                return;
            }
            if (!HasEdits)
            {
                ShowToast(Strings.ToastNothingToSave);
                return;
            }
            GdiEncoder.Format format = GdiEncoder.FindByExtension(System.IO.Path.GetExtension(_document.FilePath));
            if (format == null)
            {
                format = GdiEncoder.FindByName("PNG");
            }
            if (format == null)
            {
                ShowToast(Strings.ToastEditFailed);
                return;
            }
            string target = EditExporter.SuggestPath(_document.FilePath, format.Extension);
            EditExporter.Result result = EditExporter.Execute(_document, _recipe, target, format, 92);
            if (!result.Ok)
            {
                AppLog.Error("SaveEditedCopy " + target, new Exception(result.Error ?? "unknown"));
                ShowToast(Strings.ToastEditFailed + "：" + (result.Error ?? string.Empty));
                return;
            }
            string message = Strings.ToastEdited + "（" + result.Width + " × " + result.Height + "）";
            if (result.Capped)
            {
                message += " · 长边上限 " + EditExporter.MaxExportSide + "px";
            }
            ShowToast(message);
            SetOverlay(null);
        }

        internal void ShowInfoOverlay()
        {
            InfoOverlay overlay = new InfoOverlay();
            overlay.Fill(_document, _documentIndex, _folder.Count);
            SetOverlay(overlay);
        }

        internal void ReopenAfterResize(string path)
        {
            if (_document != null)
            {
                _document.Dispose();
                _document = null;
            }
            OpenPath(path, false);
        }

        // ---------------- 实用操作 ----------------

        internal void CopyImageToClipboard()
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            try
            {
                Clipboard.SetImage(_document.GetTier(2048) ?? _document.Preview);
                ShowToast(Strings.ToastCopied);
            }
            catch (Exception ex)
            {
                AppLog.Error("CopyImageToClipboard", ex);
                ShowToast(Strings.ToastCopyFailed);
            }
        }

        internal void RequestDelete()
        {
            if (string.IsNullOrEmpty(_documentPath) || !File.Exists(_documentPath))
            {
                return;
            }
            string target = _documentPath;
            SetOverlay(new ConfirmOverlay(Strings.DeleteTitle, Path.GetFileName(target), Strings.DeleteConfirm,
                Strings.DeleteCancel, delegate
                {
                    SetOverlay(null);
                    int code = Native.MoveToRecycleBin(Handle, target);
                    if (code != 0)
                    {
                        ShowToast(Strings.ToastDeleteFailed);
                        return;
                    }
                    ShowToast(Strings.ToastDeleted);
                    if (_document != null)
                    {
                        _document.TrimCaches();
                    }
                    NavigateRelative(1);
                }));
        }
    }
}
