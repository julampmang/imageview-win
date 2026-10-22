using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ImageView.Core
{
    /// <summary>
    /// GDI+ 解码：作为 WIC 的兜底路径（WIC 工厂创建失败时），
    /// 以及 GIF 动图的逐帧来源。
    /// 注意：GDI+ 不支持解码时缩放，超大图会先解成全尺寸，因此只在必要时使用。
    /// </summary>
    internal static class GdiDecoder
    {
        internal static bool TryProbe(string path, out int width, out int height, out int frameCount)
        {
            width = 0;
            height = 0;
            frameCount = 1;
            try
            {
                using (Image image = Image.FromFile(path))
                {
                    width = image.Width;
                    height = image.Height;
                    frameCount = GetFrameCount(image);
                    return width > 0 && height > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static Bitmap Decode(string path, int maxSide, out int sourceWidth, out int sourceHeight,
            out bool hasAlpha)
        {
            sourceWidth = 0;
            sourceHeight = 0;
            hasAlpha = false;
            try
            {
                using (Image image = Image.FromFile(path))
                {
                    sourceWidth = image.Width;
                    sourceHeight = image.Height;
                    hasAlpha = (image.PixelFormat & PixelFormat.Alpha) == PixelFormat.Alpha ||
                               image.PixelFormat == PixelFormat.Format32bppArgb ||
                               image.PixelFormat == PixelFormat.Format32bppPArgb;

                    int longest = Math.Max(image.Width, image.Height);
                    if (maxSide <= 0 || longest <= maxSide)
                    {
                        Bitmap full = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(full))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                        }
                        return full;
                    }

                    double scale = (double)maxSide / longest;
                    int tw = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int th = Math.Max(1, (int)Math.Round(image.Height * scale));
                    Bitmap scaled = new Bitmap(tw, th, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.DrawImage(image, new Rectangle(0, 0, tw, th));
                    }
                    return scaled;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("GdiDecoder.Decode " + path, ex);
                return null;
            }
        }

        /// <summary>GIF / TIFF 可能是多帧；按时间维度判断是否为动图。</summary>
        internal static int GetFrameCount(Image image)
        {
            try
            {
                FrameDimension time = new FrameDimension(FrameDimension.Time.Guid);
                int count = image.GetFrameCount(time);
                return count > 0 ? count : 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        internal static bool IsTimeBasedAnimation(string path)
        {
            try
            {
                using (Image image = Image.FromFile(path))
                {
                    return GetFrameCount(image) > 1;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>GIF 第 index 帧的延时（毫秒），读不到时用默认 100ms。</summary>
        internal static int ReadFrameDelayMs(Image image, int frameIndex)
        {
            try
            {
                PropertyItem item = image.GetPropertyItem(0x5100); // FrameDelay，单位 1/100 秒
                if (item != null && item.Value != null && item.Value.Length >= 4 * (frameIndex + 1))
                {
                    int centiseconds = BitConverter.ToInt32(item.Value, frameIndex * 4);
                    if (centiseconds > 0)
                    {
                        return centiseconds * 10;
                    }
                }
            }
            catch (Exception)
            {
            }
            return 100;
        }
    }
}
