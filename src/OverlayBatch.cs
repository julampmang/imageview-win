using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ImageView.Core;

namespace ImageView
{
    /// <summary>
    /// 批量另存：把当前目录的图片按指定长边与格式批量导出到子目录 `批量导出`。
    ///
    /// 和工作台其他部分一样，只有按下"开始"才逐张解码；一张处理完立刻释放，
    /// 所以峰值内存仍是一张图。任务在后台 STA 线程跑，进度用同步上下文回 UI 线程。
    /// </summary>
    internal sealed class BatchOverlay : Overlay
    {
        private sealed class Item
        {
            internal string Path;
            internal string Name;
            internal bool Ok;
            internal string Error;
            internal int Width;
            internal int Height;
        }

        private static readonly int[] SidePresets = new int[] { 0, 1920, 1280, 800 };
        private static readonly string[] SideLabels = new string[] { "原尺寸", "1920", "1280", "800" };

        private readonly List<Item> _items = new List<Item>();
        private readonly SynchronizationContext _uiContext;
        private readonly string _folder;
        private string _targetFolder;

        private Rectangle _card;
        private readonly Rectangle[] _sideRects = new Rectangle[4];
        private Rectangle _formatRect;
        private Rectangle _startRect;
        private Rectangle _cancelRect;
        private Rectangle _progressRect;

        private int _sideIndex = 1;
        private int _formatIndex;          // 0=跟随原格式 1=PNG 2=JPEG
        private int _hot = -1;

        private volatile bool _running;
        private volatile bool _cancelRequested;
        private int _done;
        private int _failed;
        private string _current = string.Empty;
        private bool _finished;

        internal BatchOverlay(IEnumerable<string> files)
        {
            _uiContext = SynchronizationContext.Current;
            if (files != null)
            {
                foreach (string path in files)
                {
                    Item item = new Item();
                    item.Path = path;
                    item.Name = System.IO.Path.GetFileName(path);
                    _items.Add(item);
                }
            }
            _folder = _items.Count > 0 ? System.IO.Path.GetDirectoryName(_items[0].Path) : string.Empty;
        }

        internal bool HasItems
        {
            get { return _items.Count > 0; }
        }

        internal override void Layout(ViewerForm form, Rectangle client)
        {
            int width = Ui.S(620);
            int height = Ui.S(430);
            _card = OverlayPaint.CenterCard(client, width, Math.Min(height, client.Height - Ui.S(30)));
            _targetFolder = Path.Combine(_folder ?? ".", Strings.BatchFolderName);

            int pad = Ui.S(24);
            int rowY = _card.Y + Ui.S(104);
            int pillWidth = Ui.S(92);
            int pillHeight = Ui.S(32);
            for (int i = 0; i < _sideRects.Length; i++)
            {
                _sideRects[i] = new Rectangle(_card.X + pad + i * (pillWidth + Ui.S(8)), rowY, pillWidth, pillHeight);
            }
            _formatRect = new Rectangle(_card.X + pad, rowY + Ui.S(52), Ui.S(160), pillHeight);

            _progressRect = new Rectangle(_card.X + pad, rowY + Ui.S(104), _card.Width - pad * 2, Ui.S(10));

            int buttonY = _card.Bottom - pad - Ui.S(34);
            _startRect = new Rectangle(_card.Right - pad - Ui.S(128), buttonY, Ui.S(128), Ui.S(34));
            _cancelRect = new Rectangle(_startRect.X - Ui.S(10) - Ui.S(84), buttonY, Ui.S(84), Ui.S(34));
        }

        // ---------------- 绘制 ----------------

        internal override void Paint(Graphics g, ViewerForm form)
        {
            Palette p = Theme.P;
            OverlayPaint.Scrim(g, form);
            OverlayPaint.Card(g, _card, "批量另存",
                "当前目录 " + _items.Count + " 张 · 逐张处理，内存峰值与看一张图相同");

            Rectangle info = new Rectangle(_card.X + Ui.S(24), _card.Y + Ui.S(62), _card.Width - Ui.S(48),
                Ui.S(34));
            PaintKit.Text(g, Shorten(_folder), Fonts.Small, p.TextDim, info, StringAlignment.Near,
                StringAlignment.Center, true);

            PaintKit.Text(g, "输出长边", Fonts.Small, p.Text, new Rectangle(_card.X + Ui.S(24), _card.Y + Ui.S(84),
                Ui.S(80), Ui.S(18)), StringAlignment.Near, StringAlignment.Center, false);

            for (int i = 0; i < _sideRects.Length; i++)
            {
                OverlayPaint.Button(g, _sideRects[i], SideLabels[i], i == _sideIndex, _hot == 10 + i, false);
            }

            string formatLabel = _formatIndex == 0 ? "格式：跟随原图" : _formatIndex == 1 ? "格式：PNG" : "格式：JPEG";
            OverlayPaint.Button(g, _formatRect, formatLabel, false, _hot == 20, false);

            PaintKit.Text(g, "输出到 " + Shorten(_targetFolder), Fonts.Tiny, p.TextDim,
                new Rectangle(_card.X + Ui.S(24), _card.Y + Ui.S(196), _card.Width - Ui.S(48), Ui.S(18)),
                StringAlignment.Near, StringAlignment.Center, true);

            // 进度
            PaintKit.FillRound(g, _progressRect, _progressRect.Height / 2f,
                Color.FromArgb(p.Dark ? 60 : 70, p.BorderStrong));
            float ratio = _items.Count == 0 ? 0f : (_done + _failed) / (float)_items.Count;
            int filled = (int)(_progressRect.Width * ratio);
            if (filled > 0)
            {
                PaintKit.FillRound(g, new Rectangle(_progressRect.X, _progressRect.Y, filled,
                    _progressRect.Height), _progressRect.Height / 2f, p.Accent);
            }

            string status;
            if (_running)
            {
                status = (_done + _failed) + " / " + _items.Count + "  ·  " + Shorten(_current);
            }
            else if (_finished)
            {
                status = "完成 " + _done + " 张" + (_failed > 0 ? "，失败 " + _failed + " 张" : string.Empty);
            }
            else
            {
                status = "将要处理 " + _items.Count + " 张图";
            }
            PaintKit.Text(g, status, Fonts.Small, p.Text,
                new Rectangle(_card.X + Ui.S(24), _progressRect.Bottom + Ui.S(8), _card.Width - Ui.S(48),
                    Ui.S(20)), StringAlignment.Near, StringAlignment.Center, true);

            OverlayPaint.Button(g, _cancelRect, _running ? "取消" : Strings.EditCancel, false, _hot == 30, false);
            if (_running)
            {
                OverlayPaint.Button(g, _startRect, "处理中…", true, false, false);
            }
            else if (_finished)
            {
                OverlayPaint.Button(g, _startRect, "关闭", true, _hot == 31, false);
            }
            else
            {
                OverlayPaint.Button(g, _startRect, "开始", true, _hot == 31, false);
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Length <= 78 ? text : "…" + text.Substring(text.Length - 77);
        }

        // ---------------- 输入 ----------------

        internal override bool HandleMouseDown(Point point, ViewerForm form)
        {
            if (_running)
            {
                if (_cancelRect.Contains(point))
                {
                    _cancelRequested = true;
                    return true;
                }
                return true;
            }

            for (int i = 0; i < _sideRects.Length; i++)
            {
                if (_sideRects[i].Contains(point))
                {
                    _sideIndex = i;
                    form.Invalidate();
                    return true;
                }
            }
            if (_formatRect.Contains(point))
            {
                _formatIndex = (_formatIndex + 1) % 3;
                form.Invalidate();
                return true;
            }
            if (_startRect.Contains(point))
            {
                if (_finished)
                {
                    form.SetOverlay(null);
                }
                else
                {
                    Start(form);
                }
                return true;
            }
            if (_cancelRect.Contains(point))
            {
                form.SetOverlay(null);
                return true;
            }
            if (!_card.Contains(point))
            {
                form.SetOverlay(null);
            }
            return true;
        }

        internal override bool HandleMouseMove(Point point, ViewerForm form)
        {
            int hot = -1;
            for (int i = 0; i < _sideRects.Length; i++)
            {
                if (_sideRects[i].Contains(point)) { hot = 10 + i; }
            }
            if (_formatRect.Contains(point)) { hot = 20; }
            if (_cancelRect.Contains(point)) { hot = 30; }
            if (_startRect.Contains(point)) { hot = 31; }
            if (hot != _hot)
            {
                _hot = hot;
                form.Invalidate();
            }
            return false;
        }

        internal override bool HandleKey(Keys keyData, ViewerForm form)
        {
            if (keyData == Keys.Escape)
            {
                if (_running)
                {
                    _cancelRequested = true;
                }
                else
                {
                    form.SetOverlay(null);
                }
                return true;
            }
            return false;
        }

        // ---------------- 执行 ----------------

        private void Start(ViewerForm form)
        {
            if (_items.Count == 0 || _running)
            {
                return;
            }
            _running = true;
            _cancelRequested = false;
            _finished = false;
            _done = 0;
            _failed = 0;
            form.Invalidate();

            string folder = _targetFolder;
            int side = SidePresets[_sideIndex];
            int formatIndex = _formatIndex;
            List<Item> items = _items;

            Thread worker = new Thread(delegate()
            {
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception ex)
                {
                    AppLog.Error("BatchOverlay.CreateDirectory", ex);
                    Post(form, delegate
                    {
                        _running = false;
                        _finished = true;
                        _failed = items.Count;
                        form.Invalidate();
                    });
                    return;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (_cancelRequested)
                    {
                        break;
                    }
                    Item item = items[i];
                    string current = item.Name;
                    _current = current;
                    Post(form, delegate { form.Invalidate(); });

                    RunOne(item, folder, side, formatIndex);

                    Post(form, delegate
                    {
                        if (item.Ok)
                        {
                            _done++;
                        }
                        else
                        {
                            _failed++;
                        }
                        form.Invalidate();
                    });
                }

                Post(form, delegate
                {
                    _running = false;
                    _finished = true;
                    _current = string.Empty;
                    form.Invalidate();
                });
            });
            worker.IsBackground = true;
            worker.Name = "ImageView.BatchExport";
            try
            {
                worker.SetApartmentState(ApartmentState.STA);
            }
            catch (Exception)
            {
            }
            worker.Start();
        }

        /// <summary>供自检直接调用：导出一张。</summary>
        internal static bool ExportOne(string path, string folder, int side, int formatIndex, out string error)
        {
            Item item = new Item();
            item.Path = path;
            item.Name = System.IO.Path.GetFileName(path);
            RunOne(item, folder, side, formatIndex);
            error = item.Error;
            return item.Ok;
        }

        private static void RunOne(Item item, string folder, int side, int formatIndex)
        {
            ImageDocument document = null;
            try
            {
                document = ImageDocument.Open(item.Path);
                if (document == null || !document.IsValid)
                {
                    item.Error = document != null ? document.Error : "打开失败";
                    return;
                }

                GdiEncoder.Format format;
                if (formatIndex == 1)
                {
                    format = GdiEncoder.FindByName("PNG");
                }
                else if (formatIndex == 2)
                {
                    format = GdiEncoder.FindByName("JPEG");
                }
                else
                {
                    format = GdiEncoder.FindByExtension(System.IO.Path.GetExtension(item.Path));
                    if (format == null)
                    {
                        format = GdiEncoder.FindByName("PNG");
                    }
                }
                if (format == null)
                {
                    item.Error = "无法确定输出格式";
                    return;
                }

                int width = document.PixelWidth;
                int height = document.PixelHeight;
                if (side > 0)
                {
                    double scale = side / (double)Math.Max(width, height);
                    if (scale < 1.0)
                    {
                        width = Math.Max(1, (int)Math.Round(width * scale));
                        height = Math.Max(1, (int)Math.Round(height * scale));
                    }
                }

                string name = System.IO.Path.GetFileNameWithoutExtension(item.Path) + format.Extension;
                string target = System.IO.Path.Combine(folder, name);
                if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(item.Path),
                    StringComparison.OrdinalIgnoreCase))
                {
                    target = System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(item.Path) +
                        "-导出" + format.Extension);
                }

                ResizeJob.Result result = ResizeJob.Execute(document, target, width, height, format, 92);
                if (!result.Ok)
                {
                    item.Error = result.Error;
                    return;
                }
                item.Ok = true;
                item.Width = result.Width;
                item.Height = result.Height;
            }
            catch (Exception ex)
            {
                AppLog.Error("BatchOverlay.RunOne " + item.Path, ex);
                item.Error = ex.Message;
            }
            finally
            {
                if (document != null)
                {
                    document.Dispose();
                }
            }
        }

        private void Post(ViewerForm form, SendOrPostCallback action)
        {
            if (_uiContext != null)
            {
                _uiContext.Post(action, null);
            }
            else
            {
                action(null);
            }
        }

        internal override void Detach(ViewerForm form)
        {
            _cancelRequested = true;
        }
    }
}
