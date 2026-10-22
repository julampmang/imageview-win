using System;
using System.IO;
using System.Text;

namespace ImageView.Core
{
    /// <summary>
    /// 色彩信息探测：除了 EXIF 里声明的 ColorSpace，还看容器里有没有内嵌的 ICC 配置文件。
    /// 只读文件头，不解码像素。
    /// </summary>
    internal sealed class ColorProfile
    {
        internal bool HasIcc;
        internal long IccBytes;
        internal string ContainerHint;   // 例如 "PNG 声明 sRGB"
        internal bool DeclaredSrgb;
        internal double Gamma;

        private const int MaxScanBytes = 768 * 1024;

        internal static ColorProfile Read(string path)
        {
            ColorProfile profile = new ColorProfile();
            try
            {
                byte[] data = ReadHead(path);
                if (data == null)
                {
                    return profile;
                }
                if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
                {
                    ReadJpeg(data, profile);
                }
                else if (data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                {
                    ReadPng(data, profile);
                }
                else if (data.Length > 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
                {
                    ReadWebp(data, profile);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("ColorProfile.Read " + path, ex);
            }
            return profile;
        }

        private static byte[] ReadHead(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int want = (int)Math.Min(fs.Length, MaxScanBytes);
                if (want < 32)
                {
                    return null;
                }
                byte[] buffer = new byte[want];
                int read = 0;
                while (read < want)
                {
                    int n = fs.Read(buffer, read, want - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }
                return buffer;
            }
        }

        private static void ReadJpeg(byte[] d, ColorProfile profile)
        {
            int i = 2;
            while (i + 4 <= d.Length)
            {
                if (d[i] != 0xFF)
                {
                    i++;
                    continue;
                }
                byte marker = d[i + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0xFF)
                {
                    i += 2;
                    continue;
                }
                if (marker == 0xD9 || marker == 0xDA)
                {
                    return;
                }
                int segLen = (d[i + 2] << 8) | d[i + 3];
                if (segLen < 2)
                {
                    return;
                }
                if (marker == 0xE2 && i + 16 <= d.Length)
                {
                    // "ICC_PROFILE\0" + 序号(1) + 总数(1)
                    if (d[i + 4] == 'I' && d[i + 5] == 'C' && d[i + 6] == 'C' && d[i + 7] == '_' &&
                        d[i + 8] == 'P' && d[i + 9] == 'R' && d[i + 10] == 'O' && d[i + 11] == 'F' &&
                        d[i + 12] == 'I' && d[i + 13] == 'L' && d[i + 14] == 'E' && d[i + 15] == 0)
                    {
                        profile.HasIcc = true;
                        profile.IccBytes += Math.Max(0, segLen - 16);
                    }
                }
                i += 2 + segLen;
            }
        }

        private static void ReadPng(byte[] d, ColorProfile profile)
        {
            int pos = 8;
            while (pos + 8 <= d.Length)
            {
                int size = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
                if (size < 0 || pos + 12 + size > d.Length)
                {
                    return;
                }
                string type = Encoding.ASCII.GetString(d, pos + 4, 4);
                if (type == "iCCP")
                {
                    profile.HasIcc = true;
                    profile.IccBytes += Math.Max(0, size - 20);   // 名称 + 压缩方式 + 压缩后的数据
                }
                else if (type == "sRGB")
                {
                    profile.DeclaredSrgb = true;
                    profile.ContainerHint = "PNG sRGB 块";
                }
                else if (type == "gAMA" && size >= 4)
                {
                    uint raw = (uint)((d[pos + 8] << 24) | (d[pos + 9] << 16) | (d[pos + 10] << 8) | d[pos + 11]);
                    if (raw > 0)
                    {
                        profile.Gamma = raw / 100000.0;
                        if (profile.ContainerHint == null)
                        {
                            profile.ContainerHint = "PNG gAMA";
                        }
                    }
                }
                pos += 12 + size;
            }
        }

        private static void ReadWebp(byte[] d, ColorProfile profile)
        {
            int pos = 12;
            while (pos + 8 <= d.Length)
            {
                string fourcc = Encoding.ASCII.GetString(d, pos, 4);
                int size = BitConverter.ToInt32(d, pos + 4);
                if (size < 0 || pos + 8 + size > d.Length)
                {
                    return;
                }
                if (fourcc == "ICCP")
                {
                    profile.HasIcc = true;
                    profile.IccBytes += size;
                }
                pos += 8 + size + (size & 1);
            }
        }

        /// <summary>给界面用的一句话描述。</summary>
        internal string Describe(string exifColorSpace)
        {
            if (!string.IsNullOrEmpty(exifColorSpace))
            {
                return HasIcc
                    ? exifColorSpace + "（内嵌 ICC " + FormatSize(IccBytes) + "）"
                    : exifColorSpace;
            }
            if (HasIcc)
            {
                return "内嵌 ICC " + FormatSize(IccBytes);
            }
            if (DeclaredSrgb)
            {
                return "sRGB（" + (ContainerHint ?? "容器声明") + "）";
            }
            if (Gamma > 0)
            {
                return "gamma " + Gamma.ToString("0.###") + "（" + (ContainerHint ?? "容器声明") + "）";
            }
            return null;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024)
            {
                return (bytes / 1048576.0).ToString("0.0") + " MB";
            }
            if (bytes >= 1024)
            {
                return (bytes / 1024.0).ToString("0") + " KB";
            }
            return bytes + " B";
        }
    }
}
