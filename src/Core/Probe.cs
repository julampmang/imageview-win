using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ImageView.Core
{
    /// <summary>
    /// --probe 模式：逐个打开图片并打印解码结果，用于快速验证解码链路。
    /// </summary>
    internal static class Probe
    {
        internal static int Run(string[] files)
        {
            if (files == null || files.Length == 0)
            {
                AppLog.Line("用法: ImageView.exe --probe <图片路径...>");
                return 2;
            }

            AppLog.Line("===== 解码探针 =====");
            AppLog.Line("WIC 可用: {0}", WicDecoder.Available);
            if (!WicDecoder.Available)
            {
                AppLog.Line("WIC 不可用原因: {0}", WicDecoder.LastError);
            }

            int failed = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                Doc_LastPathForProbe = path;
                AppLog.Line("");
                AppLog.Line("[{0}] {1}", i + 1, path);
                using (ImageDocument doc = ImageDocument.Open(path))
                {
                    AppLog.Line("  格式={0} 文件={1:N0}B 修改={2:yyyy-MM-dd HH:mm}",
                        doc.FormatName, doc.FileSize, doc.Modified);
                    AppLog.Line("  原始={0}x{1} 摆正后={2}x{3} 方向={4} 帧={5} 动图={6} alpha={7}",
                        doc.SourceWidth, doc.SourceHeight, doc.PixelWidth, doc.PixelHeight, doc.Orientation,
                        doc.FrameCount, doc.IsAnimated, doc.HasAlpha);
                    AppLog.Line("  解码器={0} 耗时={1}ms 错误={2}",
                        doc.UsingWic ? "WIC" : "GDI+", doc.OpenMilliseconds,
                        string.IsNullOrEmpty(doc.Error) ? "(无)" : doc.Error);

                    if (!doc.IsValid)
                    {
                        failed++;
                        continue;
                    }

                    Bitmap preview = doc.Preview;
                    AppLog.Line("  预览={0}x{1}", preview.Width, preview.Height);
                    AppLog.Line("  角点颜色: 左上={0} 右上={1} 左下={2} 右下={3}",
                        Pixel(preview, 0.12, 0.12), Pixel(preview, 0.88, 0.12),
                        Pixel(preview, 0.12, 0.88), Pixel(preview, 0.88, 0.88));
                    DumpStats(doc);
                    DumpExif(doc.Exif);

                    // 顺便验证一次“更大档位”的解码
                    if (Math.Max(doc.PixelWidth, doc.PixelHeight) > ImageDocument.PreviewMaxSide)
                    {
                        Bitmap big = doc.GetTier(4000);
                        AppLog.Line("  4000 档={0}x{1} 缓存上限={2}", big.Width, big.Height, doc.MaxCachedSide);
                    }
                }
            }

            AppLog.Line("");
            AppLog.Line(failed == 0 ? "PROBE OK" : "PROBE FAILED (" + failed + ")");
            return failed == 0 ? 0 : 1;
        }

        private static void DumpStats(ImageDocument doc)
        {
            Histogram histogram = Histogram.Compute(doc.Preview);
            if (histogram.IsEmpty)
            {
                return;
            }
            ColorProfile profile = ColorProfile.Read(doc.FilePath);
            AppLog.Line("  直方图: 采样={0} 平均亮度={1:0.0} 高光溢出={2:0.00}% 暗部溢出={3:0.00}% 峰值={4}",
                histogram.Pixels, histogram.MeanLuma, histogram.ClippedHigh * 100.0,
                histogram.ClippedLow * 100.0, histogram.Peak);
            AppLog.Line("  通道峰值位置: R={0} G={1} B={2}（0-255）",
                PeakBin(histogram.Red), PeakBin(histogram.Green), PeakBin(histogram.Blue));
            AppLog.Line("  波形网格: {0}x{1} 峰值={2}", Histogram.WaveColumns, Histogram.WaveLevels,
                histogram.WavePeak);
            AppLog.Line("  色彩: EXIF={0} ICC={1} 容器={2}",
                Show(doc.Exif != null ? doc.Exif.ColorSpace : null),
                profile.HasIcc ? ("有 " + profile.IccBytes + "B") : "无",
                Show(profile.ContainerHint));
        }
        private static void DumpExif(ExifInfo exif)
        {
            if (exif == null || !exif.HasAny)
            {
                AppLog.Line("  EXIF: （没有可读的拍摄信息）");
                return;
            }
            AppLog.Line("  EXIF 机身: {0} {1}  镜头: {2}  镜头规格: {3}",
                Show(exif.Make), Show(exif.Model), Show(exif.LensModel), Show(exif.LensSpecification));
            AppLog.Line("  EXIF 参数: 曝光={0} 光圈={1} {2} 焦距={3} {4} 补偿={5}",
                Show(exif.Exposure), Show(exif.Aperture), Show(exif.Iso), Show(exif.FocalLength),
                Show(exif.FocalLength35mm), Show(exif.ExposureBias));
            AppLog.Line("  EXIF 其它: 程序={0} 测光={1} 闪光={2} 白平衡={3} 色彩={4} 场景={5}",
                Show(exif.ExposureProgram), Show(exif.MeteringMode), Show(exif.Flash),
                Show(exif.WhiteBalance), Show(exif.ColorSpace), Show(exif.SceneCaptureType));
            AppLog.Line("  EXIF 时间: 拍摄={0} 数字化={1} 修改={2} 方向={3}",
                Show(exif.DateTimeOriginal), Show(exif.DateTimeDigitized), Show(exif.DateTimeModified),
                exif.Orientation);
            AppLog.Line("  EXIF 文件: 软件={0} 作者={1} 版权={2} EXIF尺寸={3}x{4}",
                Show(exif.Software), Show(exif.Artist), Show(exif.Copyright), exif.PixelWidth, exif.PixelHeight);
            if (exif.HasGps)
            {
                AppLog.Line("  GPS: 纬度={0} 经度={1} 海拔={2} 时间={3}",
                    Show(exif.GpsLatitudeText), Show(exif.GpsLongitudeText), Show(exif.GpsAltitudeText),
                    Show(exif.GpsTimeText));
            }
            else
            {
                AppLog.Line("  GPS: （无）");
            }
        }

        internal static string Doc_LastPathForProbe;

        private static int PeakBin(int[] bins)
        {
            int index = 0;
            for (int i = 1; i < bins.Length; i++)
            {
                if (bins[i] > bins[index])
                {
                    index = i;
                }
            }
            return index;
        }

        private static string Show(string value)
        {
            return string.IsNullOrEmpty(value) ? "—" : value;
        }

        private static string Pixel(Bitmap bmp, double fx, double fy)
        {
            try
            {
                int x = Math.Min(bmp.Width - 1, Math.Max(0, (int)(bmp.Width * fx)));
                int y = Math.Min(bmp.Height - 1, Math.Max(0, (int)(bmp.Height * fy)));
                Color c = bmp.GetPixel(x, y);
                return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            }
            catch (Exception)
            {
                return "?";
            }
        }
    }
}
