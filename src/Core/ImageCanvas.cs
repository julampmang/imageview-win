using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImageView.Core
{
    /// <summary>
    /// 画布：负责缩放、平移、视图旋转与"用哪一档位渲染"的决策。
    ///
    /// 性能要点：
    ///   - 交互中（滚轮/拖拽）用双线性 + 现有档位，保证每帧够快；
    ///   - 停止操作 120ms 后切回双三次重绘一次，拿到最终清晰度；
    ///   - 视图旋转烘焙成缓存位图，绘制永远是轴对齐的 DrawImage。
    /// </summary>
    internal sealed class ImageCanvas
    {
        internal const double MinScale = 0.05;
        internal const double MaxScale = 32.0;

        private ImageDocument _document;
        private double _scale = 1.0;
        private bool _fitMode = true;
        private PointF _center;
        private bool _centerValid;
        private int _rotation;
        private Size _viewport;

        private Bitmap _rotated;
        private Bitmap _rotatedSource;
        private int _rotatedSide;
        private int _rotatedRotation;

        private bool _highQuality = true;
        private readonly Timer _idleTimer;

        internal ImageCanvas()
        {
            _idleTimer = new Timer();
            _idleTimer.Interval = 120;
            _idleTimer.Tick += OnIdle;
        }

        internal event EventHandler NeedsRepaint;

        /// <summary>动图当前帧（由 GifAnimation 提供）；非空时优先用它绘制。</summary>
        internal Bitmap AnimationFrame;

        /// <summary>当前编辑参数（裁剪 + 颜色），由 ViewerForm 维护；null 表示原图。</summary>
        internal EditRecipe Recipe;

        internal ImageDocument Document
        {
            get { return _document; }
        }

        internal int Rotation
        {
            get { return _rotation; }
        }

        internal double Scale
        {
            get { return _scale; }
        }

        internal bool IsFitMode
        {
            get { return _fitMode; }
        }

        internal bool CanPan
        {
            get
            {
                if (_document == null || !_document.IsValid)
                {
                    return false;
                }
                SizeF size = DisplaySize();
                return size.Width > _viewport.Width + 0.5f || size.Height > _viewport.Height + 0.5f;
            }
        }

        internal void SetDocument(ImageDocument document)
        {
            if (!ReferenceEquals(_document, document))
            {
                DisposeRotated();
            }
            _document = document;
            _fitMode = true;
            _rotation = 0;
            _centerValid = false;
            _highQuality = true;
            RecalculateFit();
        }

        internal void SetViewport(Rectangle viewport)
        {
            Size size = new Size(Math.Max(1, viewport.Width), Math.Max(1, viewport.Height));
            if (size == _viewport)
            {
                return;
            }
            _viewport = size;
            if (_fitMode || !_centerValid)
            {
                RecalculateFit();
            }
            else
            {
                ClampCenter();
            }
        }

        // ---------------- 尺寸与缩放 ----------------

        /// <summary>裁剪之后的逻辑像素尺寸（未考虑视图旋转）。</summary>
        private SizeF EffectivePixelSize()
        {
            if (_document == null || !_document.IsValid)
            {
                return SizeF.Empty;
            }
            EditRecipe recipe = Recipe;
            float width = _document.PixelWidth;
            float height = _document.PixelHeight;
            if (recipe != null && recipe.HasRotation)
            {
                float scale = recipe.AutoCropScale(_document.PixelWidth, _document.PixelHeight);
                width *= scale;
                height *= scale;
            }
            if (recipe != null && recipe.HasCrop)
            {
                width *= recipe.Crop.Width;
                height *= recipe.Crop.Height;
            }
            return new SizeF(width, height);
        }

        private SizeF DisplaySize()
        {
            if (_document == null || !_document.IsValid)
            {
                return SizeF.Empty;
            }
            bool swap = _rotation == 90 || _rotation == 270;
            SizeF pixel = EffectivePixelSize();
            double iw = swap ? pixel.Height : pixel.Width;
            double ih = swap ? pixel.Width : pixel.Height;
            return new SizeF((float)(iw * _scale), (float)(ih * _scale));
        }

        private double FitScale()
        {
            if (_document == null || !_document.IsValid || _viewport.Width <= 0 || _viewport.Height <= 0)
            {
                return 1.0;
            }
            bool swap = _rotation == 90 || _rotation == 270;
            SizeF pixel = EffectivePixelSize();
            double iw = swap ? pixel.Height : pixel.Width;
            double ih = swap ? pixel.Width : pixel.Height;
            if (iw <= 0 || ih <= 0)
            {
                return 1.0;
            }
            double scale = Math.Min(_viewport.Width / iw, _viewport.Height / ih);
            // 小图不放大到糊，最多 1:1
            return Math.Min(scale, 1.0);
        }

        private void RecalculateFit()
        {
            _scale = FitScale();
            _center = new PointF(_viewport.Width / 2f, _viewport.Height / 2f);
            _centerValid = true;
            ClampCenter();
        }

        internal void ZoomToFit()
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            _fitMode = true;
            RecalculateFit();
            MarkInteraction();
        }

        internal void ZoomToActual()
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            _fitMode = false;
            _scale = 1.0;
            if (!_centerValid)
            {
                RecalculateFit();
                _fitMode = false;
            }
            ClampCenter();
            MarkInteraction();
        }

        /// <summary>以视口内某点为锚点缩放。</summary>
        internal void ZoomAt(Point anchor, double factor)
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            ZoomAround(new PointF(anchor.X, anchor.Y), factor);
        }

        /// <summary>以视口中心为锚点缩放（工具栏按钮用）。</summary>
        internal void ZoomBy(double factor)
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            ZoomAround(new PointF(_viewport.Width / 2f, _viewport.Height / 2f), factor);
        }

        /// <summary>按目标比例缩放（百分比按钮用）。</summary>
        internal void ZoomTo(double scale)
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            double target = ClampScale(scale);
            if (Math.Abs(target - _scale) < 1e-9)
            {
                return;
            }
            ZoomAround(new PointF(_viewport.Width / 2f, _viewport.Height / 2f), target / _scale);
        }

        private void ZoomAround(PointF anchor, double factor)
        {
            double oldScale = _scale;
            double newScale = ClampScale(_scale * factor);
            if (Math.Abs(newScale - oldScale) < 1e-9)
            {
                return;
            }

            SizeF oldSize = DisplaySize();
            SizeF newSize = new SizeF((float)(oldSize.Width * (newScale / oldScale)),
                (float)(oldSize.Height * (newScale / oldScale)));

            PointF oldTopLeft = new PointF(_center.X - oldSize.Width / 2f, _center.Y - oldSize.Height / 2f);
            float imageX = (anchor.X - oldTopLeft.X) / (float)oldScale;
            float imageY = (anchor.Y - oldTopLeft.Y) / (float)oldScale;

            _scale = newScale;
            _fitMode = false;
            _center = new PointF(
                anchor.X + newSize.Width / 2f - imageX * (float)newScale,
                anchor.Y + newSize.Height / 2f - imageY * (float)newScale);
            _centerValid = true;
            ClampCenter();
            MarkInteraction();
        }

        private static double ClampScale(double scale)
        {
            if (scale < MinScale)
            {
                return MinScale;
            }
            if (scale > MaxScale)
            {
                return MaxScale;
            }
            return scale;
        }

        internal void RotateClockwise()
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            _rotation = (_rotation + 90) % 360;
            DisposeRotated();
            if (_fitMode)
            {
                RecalculateFit();
            }
            else
            {
                ClampCenter();
            }
            MarkInteraction();
        }

        internal void PanBy(float dx, float dy)
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            _center = new PointF(_center.X + dx, _center.Y + dy);
            _centerValid = true;
            _fitMode = false;
            ClampCenter();
            MarkInteraction();
        }

        private void ClampCenter()
        {
            if (!_centerValid)
            {
                _center = new PointF(_viewport.Width / 2f, _viewport.Height / 2f);
                _centerValid = true;
                return;
            }
            SizeF size = DisplaySize();
            float x = _center.X;
            float y = _center.Y;

            if (size.Width <= _viewport.Width)
            {
                x = _viewport.Width / 2f;
            }
            else
            {
                float half = (size.Width - _viewport.Width) / 2f;
                if (x < _viewport.Width / 2f - half)
                {
                    x = _viewport.Width / 2f - half;
                }
                if (x > _viewport.Width / 2f + half)
                {
                    x = _viewport.Width / 2f + half;
                }
            }

            if (size.Height <= _viewport.Height)
            {
                y = _viewport.Height / 2f;
            }
            else
            {
                float half = (size.Height - _viewport.Height) / 2f;
                if (y < _viewport.Height / 2f - half)
                {
                    y = _viewport.Height / 2f - half;
                }
                if (y > _viewport.Height / 2f + half)
                {
                    y = _viewport.Height / 2f + half;
                }
            }

            _center = new PointF(x, y);
        }

        /// <summary>当前图片在视口坐标系里占据的矩形。</summary>
        internal RectangleF GetDisplayRect()
        {
            SizeF size = DisplaySize();
            return new RectangleF(_center.X - size.Width / 2f, _center.Y - size.Height / 2f, size.Width, size.Height);
        }

        // ---------------- 绘制 ----------------

        private void MarkInteraction()
        {
            _highQuality = false;
            _idleTimer.Stop();
            _idleTimer.Start();
        }

        private void OnIdle(object sender, EventArgs e)
        {
            _idleTimer.Stop();
            _highQuality = true;
            EventHandler handler = NeedsRepaint;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 绘制。<paramref name="viewport"/> 是客户区坐标（绝对），内部数学按视口原点算，
        /// 贴图前再把偏移加回目标矩形。
        ///
        /// 注意：GDI 的 StretchBlt 走的是 HDC，而 Graphics.GetHdc() 拿到的 HDC 不继承
        /// GDI+ 的 TranslateTransform。所以绝不能依赖外层 TranslateTransform 来定位图片，
        /// 否则每次局部重绘（比如只刷标题栏）图片都会按未平移的位置画出来，溢出到标题栏。
        /// </summary>
        internal void Paint(Graphics g, Rectangle viewport)
        {
            if (_document == null || !_document.IsValid)
            {
                return;
            }
            SetViewport(viewport);

            RectangleF dest = GetDisplayRect();
            dest.Offset(viewport.X, viewport.Y);
            if (dest.Width < 0.5f || dest.Height < 0.5f)
            {
                return;
            }

            GraphicsState state = g.Save();
            try
            {
                g.SetClip(viewport, CombineMode.Intersect);

                // 图片完整落在视口内时，给图片本身上一张“卡片”：圆角 + 极淡描边 + 柔和投影。
                bool card = dest.X >= viewport.X - 0.5f && dest.Y >= viewport.Y - 0.5f &&
                            dest.Right <= viewport.Right + 0.5f && dest.Bottom <= viewport.Bottom + 0.5f;
                if (card)
                {
                    // 卡片投影用“一圈柔光”代替多层扩散：多层大圆角填充每帧要 18ms，
                    // 而这里只需要极淡的一点点层次感。
                    // 半透明柔光只是一圈 2px 的底色加深，用 4 个矩形画即可：
                    // 大圆角路径填充在 GDI+ 上要几毫秒，而这里是每帧都要走的。
                    Color glow = Color.FromArgb(Theme.P.Dark ? 56 : 14, 0, 0, 0);
                    SolidBrush glowBrush = PaintKit.Brush(glow);
                    g.FillRectangle(glowBrush, dest.X - 2f, dest.Y - 1f, dest.Width + 4f, 2f);
                    g.FillRectangle(glowBrush, dest.X - 2f, dest.Bottom - 1f, dest.Width + 4f, 2f);
                    g.FillRectangle(glowBrush, dest.X - 2f, dest.Y - 1f, 2f, dest.Height + 4f);
                    g.FillRectangle(glowBrush, dest.Right, dest.Y - 1f, 2f, dest.Height + 4f);
                }

                if (_document.HasAlpha)
                {
                    PaintKit.Checker(g, dest, Math.Max(6, Ui.S(10)), Theme.P.CheckerA, Theme.P.CheckerB);
                }

                // 需要多少源像素：屏幕上的可见尺寸就是所需分辨率（留 15% 余量让降采样更细腻）。
                // 这里千万不能再除以 scale——那等于按原图分辨率解码，会白白吃掉上百 MB。
                float visibleMax = Math.Max(Math.Min(dest.Width, viewport.Width),
                    Math.Min(dest.Height, viewport.Height));
                int needed = (int)Math.Ceiling(visibleMax * 1.15f);
                Bitmap source = AnimationFrame;
                if (source == null)
                {
                    source = _document.GetTier(needed);
                }
                if (source == null)
                {
                    source = _document.Preview;
                }
                if (source == null)
                {
                    return;
                }
                if (_rotation != 0)
                {
                    source = EnsureRotated(source);
                    if (source == null)
                    {
                        return;
                    }
                }

                bool almostOneToOne = Math.Abs(_scale - 1.0) < 0.02 && _rotation == 0;

                // 只绘制视口内的那一块：放大到 400% 时 dest 可能是 32000×24000，
                // 让 GDI+ 重采样整块会白白花掉几十毫秒。
                RectangleF visible = RectangleF.Intersect(dest, new RectangleF(viewport.X, viewport.Y,
                    viewport.Width, viewport.Height));
                if (visible.Width < 0.5f || visible.Height < 0.5f)
                {
                    return;
                }

                // 裁剪：把可见区间映射到“裁剪后的有效图像”，再偏移到整张源图里的裁剪原点。
                EditRecipe recipe = Recipe;
                if (recipe != null && recipe.HasSharpen)
                {
                    source = recipe.GetDisplaySource(source);
                }
                bool rotated = recipe != null && recipe.HasRotation;
                float cropLeft = 0f;
                float cropTop = 0f;
                float cropWidth = source.Width;
                float cropHeight = source.Height;
                if (recipe != null && recipe.HasCrop)
                {
                    cropLeft = recipe.Crop.X * source.Width;
                    cropTop = recipe.Crop.Y * source.Height;
                    cropWidth = Math.Max(1f, recipe.Crop.Width * source.Width);
                    cropHeight = Math.Max(1f, recipe.Crop.Height * source.Height);
                }
                float cropRight = cropLeft + cropWidth;
                float cropBottom = cropTop + cropHeight;

                float scaleX = cropWidth / dest.Width;
                float scaleY = cropHeight / dest.Height;
                float srcX = cropLeft + (visible.X - dest.X) * scaleX;
                float srcY = cropTop + (visible.Y - dest.Y) * scaleY;
                float srcW = visible.Width * scaleX;
                float srcH = visible.Height * scaleY;

                // 源矩形必须落在位图范围内，否则 GDI+ 会抛 OutOfMemoryException
                if (srcX < cropLeft) { srcW += srcX - cropLeft; srcX = cropLeft; }
                if (srcY < cropTop) { srcH += srcY - cropTop; srcY = cropTop; }
                if (srcX + srcW > cropRight) { srcW = cropRight - srcX; }
                if (srcY + srcH > cropBottom) { srcH = cropBottom - srcY; }
                if (srcW <= 0f || srcH <= 0f)
                {
                    return;
                }

                // 有颜色调整或拉直时必须走 GDI+（StretchBlt 既没有颜色变换也不会旋转）；
                // 矩阵一次算好，逐帧只是交给 GDI+ 应用，不产生逐像素循环。
                bool needColor = recipe != null && recipe.HasColor;
                bool needTransform = rotated;

                // 1:1 或不透明图：优先走 GDI StretchBlt（比 GDI+ 快一个数量级）
                bool drawn = false;
                if (!needColor && !needTransform && !_document.HasAlpha)
                {
                    Rectangle destRect = new Rectangle((int)Math.Round(visible.X), (int)Math.Round(visible.Y),
                        Math.Max(1, (int)Math.Round(visible.Width)), Math.Max(1, (int)Math.Round(visible.Height)));
                    Rectangle srcRect = new Rectangle((int)Math.Round(srcX), (int)Math.Round(srcY),
                        Math.Max(1, (int)Math.Round(srcW)), Math.Max(1, (int)Math.Round(srcH)));
                    drawn = GdiBlitter.TryDraw(g, source, destRect, srcRect);
                }

                if (!drawn)
                {
                    // 兜底 / 带透明：GDI+
                    if (_highQuality)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = almostOneToOne ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                    }
                    else
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        g.PixelOffsetMode = almostOneToOne ? PixelOffsetMode.Half : PixelOffsetMode.HighSpeed;
                        g.CompositingQuality = CompositingQuality.HighSpeed;
                    }

                    RectangleF srcRect = new RectangleF(srcX, srcY, srcW, srcH);
                    if (needTransform)
                    {
                        // 拉直：把整张源图绕自身中心旋转，再按裁角系数缩放到 dest。
                        // 裁剪框此时是"旋转坐标系里的归一化矩形"，用平移偏移实现。
                        float autoScale = recipe.AutoCropScale(source.Width, source.Height);
                        float frameWidth = source.Width * autoScale;
                        float frameHeight = source.Height * autoScale;
                        float visibleInFrameW = frameWidth * recipe.Crop.Width;
                        float visibleInFrameH = frameHeight * recipe.Crop.Height;
                        float kx = dest.Width / Math.Max(1f, visibleInFrameW);
                        float ky = dest.Height / Math.Max(1f, visibleInFrameH);
                        float k = Math.Min(kx, ky);
                        float offX = (frameWidth / 2f - (recipe.Crop.X * frameWidth + visibleInFrameW / 2f)) * k;
                        float offY = (frameHeight / 2f - (recipe.Crop.Y * frameHeight + visibleInFrameH / 2f)) * k;

                        Matrix matrix = new Matrix();
                        matrix.Translate(-source.Width / 2f, -source.Height / 2f);
                        matrix.Scale(k, k, MatrixOrder.Append);
                        matrix.Rotate(recipe.Angle, MatrixOrder.Append);
                        matrix.Translate(dest.X + dest.Width / 2f + offX, dest.Y + dest.Height / 2f + offY,
                            MatrixOrder.Append);

                        GraphicsState rotatedState = g.Save();
                        try
                        {
                            g.Transform = matrix;
                            g.InterpolationMode = _highQuality
                                ? InterpolationMode.HighQualityBicubic
                                : InterpolationMode.Bilinear;
                            PointF[] corners = new PointF[]
                            {
                                new PointF(0f, 0f),
                                new PointF(source.Width, 0f),
                                new PointF(0f, source.Height)
                            };
                            RectangleF fullSource = new RectangleF(0f, 0f, source.Width, source.Height);
                            if (needColor)
                            {
                                using (ImageAttributes transformAttributes = new ImageAttributes())
                                {
                                    transformAttributes.SetColorMatrix(recipe.BuildMatrix(),
                                        ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                                    g.DrawImage(source, corners, fullSource, GraphicsUnit.Pixel,
                                        transformAttributes);
                                }
                            }
                            else
                            {
                                g.DrawImage(source, corners, fullSource, GraphicsUnit.Pixel);
                            }
                        }
                        finally
                        {
                            g.Restore(rotatedState);
                            matrix.Dispose();
                        }
                    }
                    else if (needColor)
                    {
                        PointF[] corners = new PointF[]
                        {
                            new PointF(visible.X, visible.Y),
                            new PointF(visible.Right, visible.Y),
                            new PointF(visible.X, visible.Bottom)
                        };
                        using (ImageAttributes attributes = new ImageAttributes())
                        {
                            attributes.SetColorMatrix(recipe.BuildMatrix(), ColorMatrixFlag.Default,
                                ColorAdjustType.Bitmap);
                            g.DrawImage(source, corners, srcRect, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    else
                    {
                        g.DrawImage(source, visible, srcRect, GraphicsUnit.Pixel);
                    }
                }

                if (card)
                {
                    PaintCardFrame(g, dest);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// 把图片四角“切”成圆角并补一圈描边。
        /// 做法是用“矩形区域减去圆角路径”得到四角的月牙，再用页面底色填掉；
        /// 这样 GDI 贴图不需要支持路径裁剪。
        /// </summary>
        private static void PaintCardFrame(Graphics g, RectangleF rect)
        {
            if (rect.Width < 8f || rect.Height < 8f)
            {
                return;
            }
            Palette p = Theme.P;
            float r = Ui.SF(12f);
            float L = rect.X, T = rect.Y, R = rect.Right, B = rect.Bottom;

            // 四个角各画一个小“月牙”把图片切圆。相比“整块区域减去圆角路径”，
            // 这样只光栅化 4 个 12×12 的小图形，实测能省下十几毫秒。
            using (SolidBrush bg = new SolidBrush(p.Backdrop))
            {
                FillCornerSliver(g, bg, L, T, r, 0);
                FillCornerSliver(g, bg, R - r, T, r, 1);
                FillCornerSliver(g, bg, R - r, B - r, r, 2);
                FillCornerSliver(g, bg, L, B - r, r, 3);
            }

            RectangleF stroke = new RectangleF(L + 0.5f, T + 0.5f,
                Math.Max(1f, rect.Width - 1f), Math.Max(1f, rect.Height - 1f));
            using (GraphicsPath border = PaintKit.RoundRect(stroke, r))
            {
                g.DrawPath(PaintKit.PenFor(Color.FromArgb(p.Dark ? 96 : 150, p.BorderStrong), 1f), border);
            }
        }

        /// <summary>画某个角的圆角“切角”三角形+弧的补块。corner: 0=左上 1=右上 2=右下 3=左下。</summary>
        private static void FillCornerSliver(Graphics g, Brush brush, float x, float y, float r, int corner)
        {
            if (r < 1f)
            {
                return;
            }
            using (GraphicsPath path = new GraphicsPath())
            {
                float d = r * 2f;
                switch (corner)
                {
                    case 0: // 左上
                        path.AddLine(x, y, x + r, y);
                        path.AddArc(x, y, d, d, 270f, -90f);
                        break;
                    case 1: // 右上
                        path.AddLine(x + r, y, x, y);
                        path.AddArc(x - r, y, d, d, 270f, 90f);
                        break;
                    case 2: // 右下
                        path.AddLine(x + r, y + r, x + r, y);
                        path.AddArc(x - r, y - r, d, d, 0f, 90f);
                        break;
                    default: // 左下
                        path.AddLine(x, y + r, x + r, y + r);
                        path.AddArc(x, y - r, d, d, 90f, 90f);
                        break;
                }
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        private Bitmap EnsureRotated(Bitmap source)
        {
            int side = Math.Max(source.Width, source.Height);
            if (_rotated != null && ReferenceEquals(_rotatedSource, source) && _rotatedSide == side &&
                _rotatedRotation == _rotation)
            {
                return _rotated;
            }
            DisposeRotated();
            try
            {
                Bitmap copy = source.Clone() as Bitmap;
                if (copy == null)
                {
                    return source;
                }
                RotateFlipType type = RotateFlipType.RotateNoneFlipNone;
                if (_rotation == 90)
                {
                    type = RotateFlipType.Rotate90FlipNone;
                }
                else if (_rotation == 180)
                {
                    type = RotateFlipType.Rotate180FlipNone;
                }
                else if (_rotation == 270)
                {
                    type = RotateFlipType.Rotate270FlipNone;
                }
                copy.RotateFlip(type);
                _rotated = copy;
                _rotatedSource = source;
                _rotatedSide = side;
                _rotatedRotation = _rotation;
                return copy;
            }
            catch (Exception ex)
            {
                AppLog.Error("ImageCanvas.EnsureRotated", ex);
                return source;
            }
        }

        private void DisposeRotated()
        {
            if (_rotated != null)
            {
                _rotated.Dispose();
                _rotated = null;
            }
            _rotatedSource = null;
            _rotatedSide = 0;
        }

        internal void Release()
        {
            _idleTimer.Stop();
            _idleTimer.Dispose();
            DisposeRotated();
            _document = null;
        }
    }
}
