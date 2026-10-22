using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ImageView.Core
{
    /// <summary>
    /// “调整尺寸并另存”：解码 → 按目标尺寸重采样 → 编码 → 原子替换。
    ///
    /// 内存关键点：源图不用全分辨率解码，而是先让 ImageDocument 出一张
    /// “刚好够用”的档位（WIC 在解码管线里缩放），再双三次缩到目标尺寸。
    /// 因此把 8000×6000 缩到 1920×1440 也不会出现 190MB 的整图。
    /// </summary>
    internal static class ResizeJob
    {
        internal sealed class Result
        {
            internal bool Ok;
            internal string Error;
            internal string Path;
            internal long Bytes;
            internal int Width;
            internal int Height;
            internal long Milliseconds;
        }

        internal const int MinSide = 1;
        internal const int MaxSide = 20000;

        internal static bool IsValidSide(int value)
        {
            return value >= MinSide && value <= MaxSide;
        }

        /// <summary>锁定比例时，根据一边算另一边（四舍五入，至少 1 像素）。</summary>
        internal static int ScaleOtherSide(int known, int knownTotal, int otherTotal)
        {
            if (knownTotal <= 0 || otherTotal <= 0)
            {
                return Math.Max(MinSide, known);
            }
            double ratio = (double)otherTotal / knownTotal;
            int value = (int)Math.Round(known * ratio);
            if (value < MinSide)
            {
                value = MinSide;
            }
            if (value > MaxSide)
            {
                value = MaxSide;
            }
            return value;
        }

        /// <summary>按百分比换算边长。</summary>
        internal static int SideForPercent(int total, int percent)
        {
            int value = (int)Math.Round(total * (percent / 100.0));
            if (value < MinSide)
            {
                value = MinSide;
            }
            if (value > MaxSide)
            {
                value = MaxSide;
            }
            return value;
        }

        internal static Result Execute(ImageDocument document, string targetPath, int width, int height,
            GdiEncoder.Format format, int quality)
        {
            Result result = new Result();
            result.Path = targetPath;
            Stopwatch watch = Stopwatch.StartNew();
            Bitmap output = null;
            try
            {
                if (document == null || !document.IsValid)
                {
                    result.Error = "当前没有可用的图片";
                    return result;
                }
                if (format == null)
                {
                    result.Error = "未选择输出格式";
                    return result;
                }
                if (!IsValidSide(width) || !IsValidSide(height))
                {
                    result.Error = Strings.ResizeRangeHint;
                    return result;
                }

                // 源档位：比目标略大一点，双三次缩下去更干净
                int needed = (int)Math.Ceiling(Math.Max(width, height) * 1.15);
                Bitmap source = document.GetTier(needed);
                if (source == null)
                {
                    source = document.Preview;
                }
                if (source == null)
                {
                    result.Error = "读取源图失败";
                    return result;
                }

                bool keepAlpha = document.HasAlpha && format.Name == "PNG";
                output = new Bitmap(width, height,
                    keepAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);

                using (Graphics g = Graphics.FromImage(output))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    if (!keepAlpha)
                    {
                        g.Clear(Color.White);
                    }
                    else
                    {
                        g.Clear(Color.Transparent);
                    }
                    using (ImageAttributes attributes = new ImageAttributes())
                    {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height,
                            GraphicsUnit.Pixel, attributes);
                    }
                }

                string error;
                if (!GdiEncoder.TryEncode(output, targetPath, format, quality, out error))
                {
                    result.Error = error;
                    return result;
                }

                watch.Stop();
                result.Ok = true;
                result.Width = width;
                result.Height = height;
                result.Milliseconds = watch.ElapsedMilliseconds;
                try
                {
                    result.Bytes = new FileInfo(targetPath).Length;
                }
                catch (Exception)
                {
                }
                return result;
            }
            catch (Exception ex)
            {
                AppLog.Error("ResizeJob.Execute", ex);
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                if (output != null)
                {
                    output.Dispose();
                }
                watch.Stop();
            }
        }

        /// <summary>默认输出文件名：原名-宽x高.扩展名。</summary>
        internal static string SuggestPath(string sourcePath, int width, int height, string extension)
        {
            try
            {
                string directory = Path.GetDirectoryName(sourcePath);
                string name = Path.GetFileNameWithoutExtension(sourcePath);
                string target = name + "-" + width + "x" + height + extension;
                string full = Path.Combine(directory ?? string.Empty, target);
                if (!File.Exists(full))
                {
                    return full;
                }
                for (int i = 2; i < 100; i++)
                {
                    string candidate = Path.Combine(directory ?? string.Empty,
                        name + "-" + width + "x" + height + "-" + i + extension);
                    if (!File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                return full;
            }
            catch (Exception)
            {
                return sourcePath + "." + extension;
            }
        }
    }
}
