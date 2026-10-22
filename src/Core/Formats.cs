using System;
using System.Collections.Generic;
using System.IO;

namespace ImageView.Core
{
    /// <summary>格式知识：扩展名、显示名、文件对话框筛选器。</summary>
    internal static class Formats
    {
        /// <summary>我们愿意尝试打开的全部扩展名（实际能否解出取决于系统解码器）。</summary>
        internal static readonly string[] SupportedExtensions = new string[]
        {
            ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff",
            ".webp", ".heic", ".heif", ".avif", ".ico", ".jfif"
        };

        private static readonly Dictionary<string, string> Names = BuildNames();

        private static Dictionary<string, string> BuildNames()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            map[".jpg"] = "JPEG";
            map[".jpeg"] = "JPEG";
            map[".jpe"] = "JPEG";
            map[".jfif"] = "JPEG";
            map[".png"] = "PNG";
            map[".gif"] = "GIF";
            map[".bmp"] = "BMP";
            map[".dib"] = "BMP";
            map[".tif"] = "TIFF";
            map[".tiff"] = "TIFF";
            map[".webp"] = "WebP";
            map[".heic"] = "HEIF";
            map[".heif"] = "HEIF";
            map[".avif"] = "AVIF";
            map[".ico"] = "ICO";
            return map;
        }

        internal static string FormatName(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                return "未知";
            }
            string name;
            if (Names.TryGetValue(ext, out name))
            {
                return name;
            }
            return ext.TrimStart('.').ToUpperInvariant();
        }

        internal static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
            {
                return false;
            }
            for (int i = 0; i < SupportedExtensions.Length; i++)
            {
                if (string.Equals(SupportedExtensions[i], ext, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>文件对话框筛选字符串。</summary>
        internal static string OpenFilter
        {
            get
            {
                List<string> all = new List<string>();
                for (int i = 0; i < SupportedExtensions.Length; i++)
                {
                    all.Add("*" + SupportedExtensions[i]);
                }
                string images = string.Join(";", all.ToArray());
                return "图片文件|" + images + "|所有文件|*.*";
            }
        }

        internal static string SupportedExtensionsText
        {
            get { return string.Join(" ", SupportedExtensions).ToUpperInvariant(); }
        }
    }
}
