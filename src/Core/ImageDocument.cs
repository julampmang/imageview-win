using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ImageView.Core
{
    /// <summary>
    /// 一张图片的运行时对象：负责“按需要的分辨率”产出位图。
    ///
    /// 三级缓存（关键设计）：
    ///   Preview —— 最长边 1600，打开即解，用于瞬时首帧与交互期绘制；
    ///   Fitted  —— 按窗口尺寸解码，窗口变大时替换；
    ///   细节档  —— 放大到 60% 以上且静止后才解码，单张预算 96MB 封顶。
    /// 之所以分档：GDI+/WIC 把 8000×6000 全解再缩到 1920 需要 500ms 以上，
    /// 而交互中每帧都这么干会卡死。
    /// </summary>
    internal sealed class ImageDocument : IDisposable
    {
        internal const int PreviewMaxSide = 1600;
        internal const long TierBudgetBytes = 64L * 1024 * 1024;

        internal string FilePath;
        internal string FileName;
        internal string FormatName;
        internal long FileSize;
        internal DateTime Modified;

        internal int SourceWidth;
        internal int SourceHeight;
        internal int PixelWidth;
        internal int PixelHeight;
        internal int Orientation = 1;
        internal bool HasAlpha;
        internal bool IsAnimated;
        internal int FrameCount = 1;
        internal bool UsingWic;
        internal string Error;
        internal ExifInfo Exif = ExifInfo.Empty();
        internal long OpenMilliseconds;

        private Bitmap _preview;
        private Bitmap _fitted;
        private int _fittedSide;
        private int _fittedRequestedSide;
        private readonly object _gate = new object();
        private bool _disposed;

        internal bool IsValid
        {
            get { return Error == null && _preview != null && PixelWidth > 0 && PixelHeight > 0; }
        }

        internal Bitmap Preview
        {
            get { return _preview; }
        }

        internal static ImageDocument Open(string path)
        {
            ImageDocument doc = new ImageDocument();
            Stopwatch watch = Stopwatch.StartNew();
            doc.FilePath = path;
            doc.FileName = Path.GetFileName(path);
            doc.FormatName = Formats.FormatName(path);

            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                {
                    doc.Error = Strings.FileMissing;
                    return doc;
                }
                doc.FileSize = info.Length;
                doc.Modified = info.LastWriteTime;
            }
            catch (Exception ex)
            {
                AppLog.Error("ImageDocument.Open 文件信息 " + path, ex);
                doc.Error = Strings.FileMissing;
                return doc;
            }

            doc.Exif = ExifInfo.Read(path);
            doc.Orientation = doc.Exif.Orientation;

            int width = 0;
            int height = 0;
            int frames = 1;
            bool probed = false;

            if (WicDecoder.Available)
            {
                probed = WicDecoder.TryProbe(path, out width, out height, out frames);
                doc.UsingWic = probed;
            }
            if (!probed)
            {
                probed = GdiDecoder.TryProbe(path, out width, out height, out frames);
                doc.UsingWic = false;
            }
            if (!probed)
            {
                doc.Error = Strings.UnsupportedFormat;
                return doc;
            }

            doc.SourceWidth = width;
            doc.SourceHeight = height;
            doc.FrameCount = frames < 1 ? 1 : frames;
            doc.IsAnimated = frames > 1;
            doc.ApplyOrientationToSize();

            bool alpha;
            Bitmap preview = doc.DecodeAt(Math.Min(PreviewMaxSide, Math.Max(doc.SourceWidth, doc.SourceHeight)), out alpha);
            if (preview == null)
            {
                doc.Error = Strings.DecodeFailedTitle;
                return doc;
            }
            doc._preview = preview;
            doc.HasAlpha = alpha;
            watch.Stop();
            doc.OpenMilliseconds = watch.ElapsedMilliseconds;
            return doc;
        }

        private void ApplyOrientationToSize()
        {
            bool swap = Orientation >= 5 && Orientation <= 8;
            PixelWidth = swap ? SourceHeight : SourceWidth;
            PixelHeight = swap ? SourceWidth : SourceHeight;
            if (PixelWidth <= 0 || PixelHeight <= 0)
            {
                PixelWidth = SourceWidth;
                PixelHeight = SourceHeight;
            }
        }

        internal static RotateFlipType MapOrientation(int orientation)
        {
            switch (orientation)
            {
                case 2:
                    return RotateFlipType.RotateNoneFlipX;
                case 3:
                    return RotateFlipType.Rotate180FlipNone;
                case 4:
                    return RotateFlipType.RotateNoneFlipY;
                case 5:
                    return RotateFlipType.Rotate90FlipX;
                case 6:
                    return RotateFlipType.Rotate90FlipNone;
                case 7:
                    return RotateFlipType.Rotate270FlipX;
                case 8:
                    return RotateFlipType.Rotate270FlipNone;
                default:
                    return RotateFlipType.RotateNoneFlipNone;
            }
        }

        /// <summary>按最长边解码（已按 EXIF 摆正）。失败返回 null。</summary>
        private Bitmap DecodeAt(int maxSide, out bool alpha)
        {
            alpha = false;
            Bitmap bitmap = null;
            int sw;
            int sh;
            bool a;

            if (WicDecoder.Available)
            {
                bitmap = WicDecoder.Decode(FilePath, maxSide, out sw, out sh, out a);
                if (bitmap != null)
                {
                    alpha = a;
                }
            }
            if (bitmap == null)
            {
                bitmap = GdiDecoder.Decode(FilePath, maxSide, out sw, out sh, out a);
                if (bitmap != null)
                {
                    alpha = a;
                    UsingWic = false;
                }
            }
            if (bitmap == null)
            {
                return null;
            }

            if (Orientation != 1)
            {
                try
                {
                    bitmap.RotateFlip(MapOrientation(Orientation));
                }
                catch (Exception ex)
                {
                    AppLog.Error("RotateFlip", ex);
                }
            }
            return bitmap;
        }

        /// <summary>
        /// 取一张“最长边至少 wantedSide”的位图。可能返回预览档（当请求更小时）。
        /// 调用方不要 Dispose 返回值，它由本文档管理。
        /// </summary>
        internal Bitmap GetTier(int wantedSide)
        {
            lock (_gate)
            {
                if (_disposed || _preview == null)
                {
                    return _preview;
                }

                int full = Math.Max(PixelWidth, PixelHeight);
                if (wantedSide <= 0 || wantedSide >= full)
                {
                    wantedSide = full;
                }
                if (wantedSide <= PreviewMaxSide)
                {
                    // 缩回小尺寸时主动释放大档，避免内存长期高位
                    if (_fitted != null && wantedSide * 2 < _fittedSide)
                    {
                        _fitted.Dispose();
                        _fitted = null;
                        _fittedSide = 0;
                        _fittedRequestedSide = 0;
                    }
                    return _preview;
                }
                // 注意：比较的是“当初请求的边长”。预算降级后实际解码边长可能更小，
                // 若拿实际边长比较就会每帧都重新解码，拖拽必然卡死。
                if (_fitted != null && _fittedRequestedSide >= wantedSide)
                {
                    return _fitted;
                }

                int side = wantedSide;
                while (side > PreviewMaxSide && EstimateBytes(side) > TierBudgetBytes)
                {
                    side = (int)(side * 0.8);
                }
                if (side <= PreviewMaxSide)
                {
                    return _preview;
                }

                bool alpha;
                Bitmap decoded = DecodeAt(side, out alpha);
                if (decoded == null)
                {
                    return _preview;
                }
                if (_fitted != null)
                {
                    _fitted.Dispose();
                }
                _fitted = decoded;
                _fittedSide = side;
                _fittedRequestedSide = wantedSide;
                return _fitted;
            }
        }

        /// <summary>当前缓存里最大的可用边长（供画布判断是否还能更清晰）。</summary>
        internal int MaxCachedSide
        {
            get
            {
                lock (_gate)
                {
                    if (_fitted != null)
                    {
                        return _fittedSide;
                    }
                    if (_preview != null)
                    {
                        return Math.Max(_preview.Width, _preview.Height);
                    }
                    return 0;
                }
            }
        }

        private long EstimateBytes(int side)
        {
            int longest = Math.Max(PixelWidth, PixelHeight);
            if (longest <= 0)
            {
                return 0;
            }
            double scale = (double)side / longest;
            if (scale > 1.0)
            {
                scale = 1.0;
            }
            long w = Math.Max(1, (long)Math.Round(PixelWidth * scale));
            long h = Math.Max(1, (long)Math.Round(PixelHeight * scale));
            return w * h * 4L;
        }

        /// <summary>导航离开时释放大档，只保留预览，回到窗口也很轻。</summary>
        internal void TrimCaches()
        {
            lock (_gate)
            {
                if (_fitted != null)
                {
                    _fitted.Dispose();
                    _fitted = null;
                    _fittedSide = 0;
                    _fittedRequestedSide = 0;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                if (_fitted != null)
                {
                    _fitted.Dispose();
                    _fitted = null;
                }
                if (_preview != null)
                {
                    _preview.Dispose();
                    _preview = null;
                }
            }
        }
    }
}
