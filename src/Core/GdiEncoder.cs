using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ImageView.Core
{
    /// <summary>
    /// 输出格式探测与编码。用的是 GDI+ 自带的编码器（系统必有 JPEG/PNG/BMP/GIF/TIFF），
    /// 运行期枚举“这台机器真正能写哪些格式”，所以列表是自适应的。
    /// </summary>
    internal static class GdiEncoder
    {
        internal sealed class Format
        {
            internal string Name;
            internal string Extension;
            internal ImageCodecInfo Codec;
            internal bool SupportsQuality;

            internal string DisplayName
            {
                get { return Name; }
            }
        }

        private static List<Format> _formats;

        /// <summary>当前系统可用的输出格式（按固定优先级排序）。</summary>
        internal static List<Format> Formats()
        {
            if (_formats != null)
            {
                return _formats;
            }
            List<Format> list = new List<Format>();
            try
            {
                ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                for (int i = 0; i < codecs.Length; i++)
                {
                    Format format = FromCodec(codecs[i]);
                    if (format != null)
                    {
                        list.Add(format);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("GdiEncoder.Formats", ex);
            }

            // 保证顺序稳定：JPEG, PNG, BMP, TIFF, GIF
            list.Sort(delegate(Format a, Format b) { return Order(a.Name).CompareTo(Order(b.Name)); });
            _formats = list;
            return _formats;
        }

        private static int Order(string name)
        {
            switch (name)
            {
                case "JPEG":
                    return 0;
                case "PNG":
                    return 1;
                case "BMP":
                    return 2;
                case "TIFF":
                    return 3;
                case "GIF":
                    return 4;
                default:
                    return 9;
            }
        }

        private static Format FromCodec(ImageCodecInfo codec)
        {
            if (codec == null || codec.FormatDescription == null)
            {
                return null;
            }
            string description = codec.FormatDescription.ToUpperInvariant();
            string name;
            string extension;
            bool quality = false;
            if (description.Contains("JPEG"))
            {
                name = "JPEG";
                extension = ".jpg";
                quality = true;
            }
            else if (description.Contains("PNG"))
            {
                name = "PNG";
                extension = ".png";
            }
            else if (description.Contains("BMP"))
            {
                name = "BMP";
                extension = ".bmp";
            }
            else if (description.Contains("TIFF"))
            {
                name = "TIFF";
                extension = ".tif";
            }
            else if (description.Contains("GIF"))
            {
                name = "GIF";
                extension = ".gif";
            }
            else
            {
                return null;
            }

            Format format = new Format();
            format.Name = name;
            format.Extension = extension;
            format.Codec = codec;
            format.SupportsQuality = quality;
            return format;
        }

        internal static Format FindByExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return null;
            }
            string normalized = extension.ToLowerInvariant();
            if (normalized == ".jpeg")
            {
                normalized = ".jpg";
            }
            if (normalized == ".tiff")
            {
                normalized = ".tif";
            }
            List<Format> formats = Formats();
            for (int i = 0; i < formats.Count; i++)
            {
                if (formats[i].Extension == normalized)
                {
                    return formats[i];
                }
            }
            return null;
        }

        internal static Format FindByName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            List<Format> formats = Formats();
            for (int i = 0; i < formats.Count; i++)
            {
                if (string.Equals(formats[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return formats[i];
                }
            }
            return null;
        }

        /// <summary>把位图编码写入 path（先写 .tmp 再替换，保证不留下半个文件）。</summary>
        internal static bool TryEncode(Bitmap bitmap, string path, Format format, int quality, out string error)
        {
            error = null;
            if (bitmap == null || format == null || string.IsNullOrEmpty(path))
            {
                error = "参数无效";
                return false;
            }

            string temp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                EncoderParameters parameters = null;
                if (format.SupportsQuality)
                {
                    parameters = new EncoderParameters(1);
                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,
                        (long)Math.Max(1, Math.Min(100, quality)));
                }

                using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    if (parameters != null)
                    {
                        bitmap.Save(stream, format.Codec, parameters);
                    }
                    else
                    {
                        bitmap.Save(stream, format.Codec, null);
                    }
                    stream.Flush();
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, null, true);
                }
                else
                {
                    File.Move(temp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("GdiEncoder.TryEncode " + path, ex);
                error = ex.Message;
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch (Exception)
                {
                }
                return false;
            }
            finally
            {
                // EncoderParameters 实现 IDisposable，但 Param 数组里的 EncoderParameter 也需要释放
                if (format != null)
                {
                }
            }
        }
    }
}
