using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ImageView.Core
{
    /// <summary>
    /// 编辑参数（非破坏）。所有颜色调整都合成成**一次 ColorMatrix**：
    /// 亮度/对比度/饱和度/色温本身就是线性变换，自动色阶是"每通道线性拉伸"，
    /// 也能写成矩阵——所以预览不需要逐像素循环、不需要额外缓冲区，
    /// 放大到 400% 也不会因为调整而变慢。
    ///
    /// 裁剪只是矩形，绘制时换成源矩形即可，同样零像素成本。
    /// </summary>
    internal sealed class EditRecipe
    {
        internal const float SliderMin = -100f;
        internal const float SliderMax = 100f;

        internal float Brightness;
        internal float Contrast;
        internal float Saturation;
        internal float Warmth;

        /// <summary>拉直角度（度，正数顺时针），-45..45。</summary>
        internal float Angle;

        /// <summary>锐化强度 0..100（USM 叠加系数 0..2）。</summary>
        internal float Sharpen;

        /// <summary>裁剪区域，归一化到 [0,1]（相对当前已裁剪后的图像）。</summary>
        internal RectangleF Crop = new RectangleF(0f, 0f, 1f, 1f);

        /// <summary>自动色阶产生的每通道线性拉伸（增益 + 偏移），未应用时为单位值。</summary>
        private float[] _gain = new float[] { 1f, 1f, 1f };
        private float[] _bias = new float[] { 0f, 0f, 0f };

        internal bool AutoApplied;

        internal bool HasCrop
        {
            get
            {
                return Crop.X > 0.0005f || Crop.Y > 0.0005f ||
                       Crop.Width < 0.999f || Crop.Height < 0.999f;
            }
        }

        internal bool HasColor
        {
            get
            {
                return Math.Abs(Brightness) > 0.01f || Math.Abs(Contrast) > 0.01f ||
                       Math.Abs(Saturation) > 0.01f || Math.Abs(Warmth) > 0.01f || AutoApplied;
            }
        }

        internal bool HasRotation
        {
            get { return Math.Abs(Angle) > 0.01f; }
        }

        internal bool HasSharpen
        {
            get { return Sharpen > 0.01f; }
        }

        internal bool IsIdentity
        {
            get { return !HasCrop && !HasColor && !HasRotation && !HasSharpen; }
        }

        internal void ResetColor()
        {
            Brightness = 0f;
            Contrast = 0f;
            Saturation = 0f;
            Warmth = 0f;
            _gain = new float[] { 1f, 1f, 1f };
            _bias = new float[] { 0f, 0f, 0f };
            AutoApplied = false;
        }

        internal void ResetAll()
        {
            ResetColor();
            Crop = new RectangleF(0f, 0f, 1f, 1f);
            Angle = 0f;
            Sharpen = 0f;
            ReleaseSharpened();
        }

        /// <summary>
        /// 旋转后能内接的、保持原比例的矩形占原图的比例（也就是"裁掉四个黑角"的系数）。
        /// </summary>
        internal float AutoCropScale(int width, int height)
        {
            if (!HasRotation || width <= 0 || height <= 0)
            {
                return 1f;
            }
            double rad = Math.Abs(Angle) * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double boxW = width * cos + height * sin;
            double boxH = width * sin + height * cos;
            double scale = Math.Min(width / boxW, height / boxH);
            if (scale > 1.0) { scale = 1.0; }
            if (scale < 0.1) { scale = 0.1; }
            return (float)scale;
        }

        // ---------- 锐化预览缓存（一次只留一张，且限制边长） ----------

        private Bitmap _sharpened;
        private Bitmap _sharpenedSource;
        private float _sharpenedAmount;

        /// <summary>拖动锐化滑杆时临时关掉预览（卷积太慢，会掉帧）。</summary>
        internal bool SuppressSharpenPreview;

        internal void ReleaseSharpened()
        {
            if (_sharpened != null)
            {
                _sharpened.Dispose();
                _sharpened = null;
            }
            _sharpenedSource = null;
        }

        /// <summary>
        /// 给画布用的"显示源"：有锐化时返回缓存好的锐化副本，否则原样返回。
        /// 只对边长 ≤ PreviewMaxSide 的源做，避免为了预览多占几十 MB。
        /// </summary>
        internal Bitmap GetDisplaySource(Bitmap source)
        {
            if (source == null || !HasSharpen || SuppressSharpenPreview)
            {
                return source;
            }
            if (Math.Max(source.Width, source.Height) > UnsharpMask.PreviewMaxSide)
            {
                return source;
            }
            if (_sharpened != null && ReferenceEquals(_sharpenedSource, source) &&
                Math.Abs(_sharpenedAmount - Sharpen) < 0.01f)
            {
                return _sharpened;
            }
            ReleaseSharpened();
            Bitmap result = UnsharpMask.Apply(source, Sharpen / 100f * 1.5f);
            if (!ReferenceEquals(result, source))
            {
                _sharpened = result;
                _sharpenedSource = source;
                _sharpenedAmount = Sharpen;
                return result;
            }
            return source;
        }

        internal EditRecipe Clone()
        {
            EditRecipe copy = (EditRecipe)MemberwiseClone();
            copy._gain = new float[] { _gain[0], _gain[1], _gain[2] };
            copy._bias = new float[] { _bias[0], _bias[1], _bias[2] };
            copy._sharpened = null;
            copy._sharpenedSource = null;
            return copy;
        }

        /// <summary>
        /// 自动色阶：按每通道 0.6% / 99.4% 分位把黑点白点拉满。
        /// 直接生成线性拉伸参数，不产生任何新位图。
        /// </summary>
        internal bool TryAutoLevels(Histogram histogram)
        {
            if (histogram == null || histogram.IsEmpty)
            {
                return false;
            }
            long total = histogram.Pixels;
            float[] gain = new float[3];
            float[] bias = new float[3];
            int[][] channels = new int[][] { histogram.Red, histogram.Green, histogram.Blue };

            for (int c = 0; c < 3; c++)
            {
                int[] bins = channels[c];
                long lowTarget = (long)(total * 0.006);
                long highTarget = (long)(total * 0.994);
                int lo = 0;
                int hi = 255;
                long acc = 0;
                for (int i = 0; i < 256; i++)
                {
                    acc += bins[i];
                    if (acc >= lowTarget) { lo = i; break; }
                }
                acc = 0;
                for (int i = 255; i >= 0; i--)
                {
                    acc += bins[i];
                    if (acc >= total - highTarget) { hi = i; break; }
                }

                // 已经吃满动态范围的通道不要硬拉，否则会把噪点放大
                if (hi - lo < 8)
                {
                    gain[c] = 1f;
                    bias[c] = 0f;
                    continue;
                }
                float g = 255f / (hi - lo);
                if (g < 0.6f) { g = 0.6f; }
                if (g > 4f) { g = 4f; }
                gain[c] = g;
                bias[c] = -lo * g / 255f;
            }

            _gain = gain;
            _bias = bias;
            AutoApplied = true;
            return true;
        }

        /// <summary>合成最终的 ColorMatrix（GDI+ 用行向量：[R G B A 1] × M）。</summary>
        internal ColorMatrix BuildMatrix()
        {
            // K: 3x3 线性部分；T: 3 维偏移（归一化到 0..1）
            float[,] k = new float[3, 3];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    k[i, j] = i == j ? 1f : 0f;
                }
            }
            float[] t = new float[3];

            // 自动色阶（每通道增益 + 偏移）
            if (AutoApplied)
            {
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        k[i, j] *= _gain[i];
                    }
                    t[i] = t[i] * _gain[i] + _bias[i];
                }
            }

            // 色温：整体偏暖 = 抬红压蓝，偏冷反之
            float warm = Warmth / 100f;
            if (Math.Abs(warm) > 0.0001f)
            {
                float[] wg = new float[] { 1f + warm * 0.25f, 1f, 1f - warm * 0.25f };
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        k[i, j] *= wg[i];
                    }
                    t[i] *= wg[i];
                }
            }

            // 对比度：绕中灰(0.5)缩放
            float f = (100f + Contrast) / 100f;
            if (f < 0.1f) { f = 0.1f; }
            if (f > 3f) { f = 3f; }
            if (Math.Abs(f - 1f) > 0.0001f)
            {
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        k[i, j] *= f;
                    }
                    t[i] = t[i] * f + 0.5f * (1f - f);
                }
            }

            // 亮度：整体加性偏移
            float bright = Brightness / 100f * 0.5f;
            if (Math.Abs(bright) > 0.0001f)
            {
                for (int i = 0; i < 3; i++)
                {
                    t[i] += bright;
                }
            }

            // 饱和度：向亮度（Rec.601）插值
            float s = 1f + Saturation / 100f;
            if (s < 0f) { s = 0f; }
            if (s > 3f) { s = 3f; }
            if (Math.Abs(s - 1f) > 0.0001f)
            {
                const float lr = 0.299f;
                const float lg = 0.587f;
                const float lb = 0.114f;
                float[,] sat = new float[3, 3];
                sat[0, 0] = lr * (1f - s) + s; sat[0, 1] = lg * (1f - s); sat[0, 2] = lb * (1f - s);
                sat[1, 0] = lr * (1f - s); sat[1, 1] = lg * (1f - s) + s; sat[1, 2] = lb * (1f - s);
                sat[2, 0] = lr * (1f - s); sat[2, 1] = lg * (1f - s); sat[2, 2] = lb * (1f - s) + s;

                float[,] nk = new float[3, 3];
                float[] nt = new float[3];
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        float sum = 0f;
                        for (int m = 0; m < 3; m++)
                        {
                            sum += k[i, m] * sat[m, j];
                        }
                        nk[i, j] = sum;
                    }
                    float ts = 0f;
                    for (int m = 0; m < 3; m++)
                    {
                        ts += t[m] * sat[m, i];
                    }
                    nt[i] = ts;
                }
                k = nk;
                t = nt;
            }

            // 注意：GDI+ 的 ColorMatrix 按行向量作用——out_j = Σ_i in_i * M[i, j]。
            // 我们的 k 是按“out_i = Σ_j k[i, j] * in_j”推出来的，所以这里要转置填入；
            // 填错的话饱和度会串到别的通道（红变绿那种）。
            ColorMatrix matrix = new ColorMatrix();
            matrix.Matrix00 = k[0, 0]; matrix.Matrix10 = k[0, 1]; matrix.Matrix20 = k[0, 2];
            matrix.Matrix01 = k[1, 0]; matrix.Matrix11 = k[1, 1]; matrix.Matrix21 = k[1, 2];
            matrix.Matrix02 = k[2, 0]; matrix.Matrix12 = k[2, 1]; matrix.Matrix22 = k[2, 2];
            matrix.Matrix40 = t[0];
            matrix.Matrix41 = t[1];
            matrix.Matrix42 = t[2];
            return matrix;
        }

        /// <summary>把参数画进目标位图（导出用，逐像素一次性完成，不经过 GDI+ 缩放）。</summary>
        internal Bitmap Apply(Bitmap source, Rectangle cropPixels)
        {
            if (source == null)
            {
                return null;
            }
            Rectangle area = cropPixels;
            if (area.Width <= 0 || area.Height <= 0 || area.X < 0 || area.Y < 0 ||
                area.Right > source.Width || area.Bottom > source.Height)
            {
                area = new Rectangle(0, 0, source.Width, source.Height);
            }

            Bitmap output = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
            ColorMatrix matrix = BuildMatrix();
            using (Graphics g = Graphics.FromImage(output))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area.X, area.Y, area.Width,
                    area.Height, GraphicsUnit.Pixel, attributes);
            }
            return output;
        }

        /// <summary>把源图旋转并按 AutoCropScale 裁掉黑角，输出尺寸就是裁角后的尺寸。</summary>
        internal Bitmap Rotate(Bitmap source)
        {
            if (source == null || !HasRotation)
            {
                return null;
            }
            float scale = AutoCropScale(source.Width, source.Height);
            int outW = Math.Max(1, (int)Math.Round(source.Width * scale));
            int outH = Math.Max(1, (int)Math.Round(source.Height * scale));
            Bitmap output = null;
            try
            {
                output = new Bitmap(outW, outH, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(output))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    // 缩放比例：旋转坐标系里的整幅尺寸 → 输出尺寸
                    float k = outW / (float)Math.Max(1f, source.Width * AutoCropScale(source.Width, source.Height));
                    Matrix matrix = new Matrix();
                    matrix.Translate(-source.Width / 2f, -source.Height / 2f);
                    matrix.Scale(k, k, MatrixOrder.Append);
                    matrix.Rotate(Angle, MatrixOrder.Append);
                    matrix.Translate(outW / 2f, outH / 2f, MatrixOrder.Append);
                    g.Transform = matrix;
                    g.DrawImage(source, new RectangleF(0f, 0f, source.Width, source.Height),
                        new RectangleF(0f, 0f, source.Width, source.Height), GraphicsUnit.Pixel);
                }
                return output;
            }
            catch (Exception ex)
            {
                AppLog.Error("EditRecipe.Rotate", ex);
                if (output != null)
                {
                    output.Dispose();
                }
                return null;
            }
        }

        /// <summary>按归一化裁剪框算出像素矩形。</summary>
        internal Rectangle CropPixels(int width, int height)
        {
            int x = (int)Math.Round(Crop.X * width);
            int y = (int)Math.Round(Crop.Y * height);
            int w = (int)Math.Round(Crop.Width * width);
            int h = (int)Math.Round(Crop.Height * height);
            if (w < 1) { w = 1; }
            if (h < 1) { h = 1; }
            if (x < 0) { x = 0; }
            if (y < 0) { y = 0; }
            if (x + w > width) { w = width - x; }
            if (y + h > height) { h = height - y; }
            return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
        }
    }
}
