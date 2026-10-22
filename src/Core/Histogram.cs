using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ImageView.Core
{
    /// <summary>
    /// 直方图。EXIF 里没有这种东西，是从解码后的像素统计出来的：
    /// 亮度按 Rec.601 加权（0.299/0.587/0.114），同时统计 R/G/B 三通道，
    /// 顺便给出平均亮度与两端溢出比例（判断过曝/欠曝）。
    /// </summary>
    internal sealed class Histogram
    {
        internal readonly int[] Red = new int[256];
        internal readonly int[] Green = new int[256];
        internal readonly int[] Blue = new int[256];
        internal readonly int[] Luma = new int[256];

        /// <summary>波形图网格：横向 192 列（对应画面左右），纵向 128 级亮度。</summary>
        internal const int WaveColumns = 192;
        internal const int WaveLevels = 128;
        internal readonly int[] Wave = new int[WaveColumns * WaveLevels];
        internal int WavePeak = 1;

        internal long Pixels;
        internal int Peak = 1;
        internal double MeanLuma;
        internal double ClippedHigh;
        internal double ClippedLow;

        internal static Histogram Compute(Bitmap bitmap)
        {
            Histogram histogram = new Histogram();
            if (bitmap == null)
            {
                return histogram;
            }
            try
            {
                Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int stride = data.Stride;
                    int width = bitmap.Width;
                    int height = bitmap.Height;
                    byte[] row = new byte[Math.Abs(stride)];
                    long sum = 0;
                    long clippedHigh = 0;
                    long clippedLow = 0;

                    // 大图抽样即可：每 2 行取 2 列，直方图形状不受影响，速度快 4 倍
                    int stepY = height > 900 ? 2 : 1;
                    int stepX = width > 1400 ? 2 : 1;

                    for (int y = 0; y < height; y += stepY)
                    {
                        IntPtr line = new IntPtr(data.Scan0.ToInt64() + (long)y * stride);
                        System.Runtime.InteropServices.Marshal.Copy(line, row, 0, row.Length);
                        for (int x = 0; x < width; x += stepX)
                        {
                            int i = x * 4;
                            if (i + 3 >= row.Length)
                            {
                                break;
                            }
                            byte a = row[i + 3];
                            if (a == 0)
                            {
                                continue;
                            }
                            int b = row[i];
                            int g = row[i + 1];
                            int r = row[i + 2];
                            if (a != 255)
                            {
                                // 预乘 alpha 还原真实颜色
                                float k = 255f / a;
                                b = (int)(b * k);
                                g = (int)(g * k);
                                r = (int)(r * k);
                            }
                            if (r > 255) { r = 255; }
                            if (g > 255) { g = 255; }
                            if (b > 255) { b = 255; }

                            histogram.Red[r]++;
                            histogram.Green[g]++;
                            histogram.Blue[b]++;
                            int luma = (r * 299 + g * 587 + b * 114) / 1000;
                            histogram.Luma[luma]++;
                            histogram.Pixels++;
                            int column = (int)((long)x * WaveColumns / width);
                            if (column < 0) { column = 0; }
                            if (column >= WaveColumns) { column = WaveColumns - 1; }
                            int level = luma * WaveLevels / 256;
                            if (level >= WaveLevels) { level = WaveLevels - 1; }
                            histogram.Wave[column * WaveLevels + level]++;
                            sum += luma;
                            if (luma >= 250) { clippedHigh++; }
                            if (luma <= 4) { clippedLow++; }
                        }
                    }

                    if (histogram.Pixels > 0)
                    {
                        histogram.MeanLuma = (double)sum / histogram.Pixels;
                        histogram.ClippedHigh = (double)clippedHigh / histogram.Pixels;
                        histogram.ClippedLow = (double)clippedLow / histogram.Pixels;
                    }

                    // 峰值取三个通道的最大值，保证三色标尺一致
                    int peak = 1;
                    for (int i = 0; i < 256; i++)
                    {
                        if (histogram.Luma[i] > peak) { peak = histogram.Luma[i]; }
                        if (histogram.Red[i] > peak) { peak = histogram.Red[i]; }
                        if (histogram.Green[i] > peak) { peak = histogram.Green[i]; }
                        if (histogram.Blue[i] > peak) { peak = histogram.Blue[i]; }
                    }
                    histogram.Peak = peak;

                    int wavePeak = 1;
                    for (int i = 0; i < histogram.Wave.Length; i++)
                    {
                        if (histogram.Wave[i] > wavePeak) { wavePeak = histogram.Wave[i]; }
                    }
                    histogram.WavePeak = wavePeak;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Histogram.Compute", ex);
            }
            return histogram;
        }

        internal bool IsEmpty
        {
            get { return Pixels == 0; }
        }

        private Bitmap _waveBitmap;

        /// <summary>
        /// 把波形网格渲染成一张小位图（192×128），绘制时再放大。
        /// 逐格 FillRectangle 要好几万次 GDI+ 调用，先画进小图再缩放会快两个数量级。
        /// </summary>
        internal Bitmap RenderWaveBitmap(Color ink)
        {
            if (_waveBitmap != null)
            {
                return _waveBitmap;
            }
            Bitmap bitmap = new Bitmap(WaveColumns, WaveLevels, PixelFormat.Format32bppPArgb);
            try
            {
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, WaveColumns, WaveLevels),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int stride = data.Stride;
                    byte[] row = new byte[Math.Abs(stride)];
                    for (int y = 0; y < WaveLevels; y++)
                    {
                        Array.Clear(row, 0, row.Length);
                        // 屏幕坐标向下 = 亮度更低
                        int level = WaveLevels - 1 - y;
                        for (int column = 0; column < WaveColumns; column++)
                        {
                            int count = Wave[column * WaveLevels + level];
                            if (count <= 0)
                            {
                                continue;
                            }
                            double ratio = Math.Sqrt((double)count / WavePeak);
                            int alpha = (int)Math.Min(255, 40 + ratio * 215);
                            int i = column * 4;
                            // 预乘 alpha
                            row[i] = (byte)(ink.B * alpha / 255);
                            row[i + 1] = (byte)(ink.G * alpha / 255);
                            row[i + 2] = (byte)(ink.R * alpha / 255);
                            row[i + 3] = (byte)alpha;
                        }
                        System.Runtime.InteropServices.Marshal.Copy(row, 0,
                            new IntPtr(data.Scan0.ToInt64() + (long)y * stride), row.Length);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }                _waveBitmap = bitmap;
                return _waveBitmap;
            }
            catch (Exception ex)
            {
                AppLog.Error("Histogram.RenderWaveBitmap", ex);
                bitmap.Dispose();
                return null;
            }
        }

        /// <summary>释放内部缓存位图。</summary>
        internal void Dispose()
        {
            if (_waveBitmap != null)
            {
                _waveBitmap.Dispose();
                _waveBitmap = null;
            }
        }
    }
}
