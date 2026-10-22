using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ImageView.Core
{
    /// <summary>
    /// USM 锐化（只在保存时、或调整面板松手后对预览做一次）。
    /// out = src + amount * (src - blur)，blur 是可分离的 1-2-1 高斯。
    /// 全程 LockBits + 字节数组，不碰 GDI+ 的逐像素接口。
    /// </summary>
    internal static class UnsharpMask
    {
        /// <summary>超过这个边长的位图不做实时预览锐化，避免多占几十 MB。</summary>
        internal const int PreviewMaxSide = 2000;

        internal static Bitmap Apply(Bitmap source, float amount)
        {
            if (source == null || amount <= 0.001f)
            {
                return source;
            }
            if (amount > 2f)
            {
                amount = 2f;
            }
            int width = source.Width;
            int height = source.Height;
            if (width < 3 || height < 3)
            {
                return source;
            }

            Bitmap copy = null;
            try
            {
                copy = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(copy))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(source, 0, 0);
                }

                BitmapData data = copy.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite,
                    PixelFormat.Format32bppPArgb);
                try
                {
                    int stride = data.Stride;
                    int bytes = Math.Abs(stride) * height;
                    byte[] pixels = new byte[bytes];
                    byte[] horizontal = new byte[bytes];
                    byte[] blurred = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, bytes);

                    BlurHorizontal(pixels, horizontal, width, height, stride);
                    BlurVertical(horizontal, blurred, width, height, stride);

                    for (int y = 0; y < height; y++)
                    {
                        int row = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int i = row + x * 4;
                            for (int c = 0; c < 3; c++)
                            {
                                int value = pixels[i + c];
                                pixels[i + c] = Clamp(value + (int)((value - blurred[i + c]) * amount));
                            }
                        }
                    }

                    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, bytes);
                }
                finally
                {
                    copy.UnlockBits(data);
                }
                return copy;
            }
            catch (Exception ex)
            {
                AppLog.Error("Sharpen.Apply", ex);
                if (copy != null)
                {
                    copy.Dispose();
                }
                return source;
            }
        }

        /// <summary>横向 1-2-1 模糊。</summary>
        private static void BlurHorizontal(byte[] src, byte[] dest, int width, int height, int stride)
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x * 4;
                    int left = x > 0 ? i - 4 : i;
                    int right = x < width - 1 ? i + 4 : i;
                    for (int c = 0; c < 3; c++)
                    {
                        dest[i + c] = (byte)((src[left + c] + 2 * src[i + c] + src[right + c]) >> 2);
                    }
                    dest[i + 3] = src[i + 3];
                }
            }
        }

        /// <summary>纵向 1-2-1 模糊（src 已经是横向模糊结果）。</summary>
        private static void BlurVertical(byte[] src, byte[] dest, int width, int height, int stride)
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                int up = (y > 0 ? y - 1 : y) * stride;
                int down = (y < height - 1 ? y + 1 : y) * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x * 4;
                    int u = up + x * 4;
                    int d = down + x * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        dest[i + c] = (byte)((src[u + c] + 2 * src[i + c] + src[d + c]) >> 2);
                    }
                    dest[i + 3] = src[i + 3];
                }
            }
        }

        private static byte Clamp(int value)
        {
            if (value < 0)
            {
                return 0;
            }
            if (value > 255)
            {
                return 255;
            }
            return (byte)value;
        }
    }
}
