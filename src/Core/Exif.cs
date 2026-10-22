using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ImageView.Core
{
    /// <summary>
    /// EXIF/IFD 解析（自己按字节读，不依赖 GDI+/WIC 的元数据接口）。
    ///
    /// 支持容器：JPEG(APP1) / TIFF / PNG(eXIf 块) / WebP(RIFF 里的 EXIF 块)。
    /// 读取内容：方向、机身与镜头、拍摄时间、曝光三要素、曝光补偿、闪光、测光、白平衡、
    /// 色彩空间、场景类型、EXIF 像素尺寸、以及 GPS 经纬度/海拔/时间。
    ///
    /// HEIC/HEIF 的 EXIF 存在 meta box 里，本程序不解析（这类文件不显示拍摄信息）。
    /// </summary>
    internal sealed class ExifInfo
    {
        internal int Orientation = 1;

        // 机身 / 软件 / 作者
        internal string Make;
        internal string Model;
        internal string LensModel;
        internal string LensSpecification;
        internal string Software;
        internal string Artist;
        internal string Copyright;

        // 时间
        internal string DateTimeOriginal;
        internal string DateTimeDigitized;
        internal string DateTimeModified;

        // 拍摄参数
        internal string Exposure;
        internal string Aperture;
        internal string Iso;
        internal string FocalLength;
        internal string FocalLength35mm;
        internal string ExposureBias;
        internal string ExposureProgram;
        internal string MeteringMode;
        internal string Flash;
        internal string WhiteBalance;
        internal string ColorSpace;
        internal string SceneCaptureType;

        // EXIF 里记录的像素尺寸（一般等于实际尺寸）
        internal int PixelWidth;
        internal int PixelHeight;

        // GPS
        internal bool HasGps;
        internal double GpsLatitude;
        internal double GpsLongitude;
        internal string GpsLatitudeText;
        internal string GpsLongitudeText;
        internal string GpsAltitudeText;
        internal string GpsTimeText;

        internal bool HasAny;

        private const int MaxHeaderBytes = 512 * 1024;

        internal static ExifInfo Empty()
        {
            return new ExifInfo();
        }

        internal static ExifInfo Read(string path)
        {
            ExifInfo info = new ExifInfo();
            try
            {
                byte[] data = ReadHead(path);
                if (data == null)
                {
                    return info;
                }

                int tiffStart = FindTiffStart(data);
                if (tiffStart >= 0 && tiffStart + 8 <= data.Length)
                {
                    new TiffReader(data, tiffStart, info).Parse();
                }


                info.Finish();
            }
            catch (Exception ex)
            {
                AppLog.Error("ExifInfo.Read " + path, ex);
            }
            return info;
        }

        /// <summary>把数值转成展示文案，并判定是否值得显示。</summary>
        private void Finish()
        {
            GpsLatitudeText = GpsLatitude > 0
                ? (hasLatSouth ? -GpsLatitude : GpsLatitude).ToString("0.000000", CultureInfo.InvariantCulture) + "°"
                : null;
            GpsLongitudeText = GpsLongitude > 0
                ? (hasLonWest ? -GpsLongitude : GpsLongitude).ToString("0.000000", CultureInfo.InvariantCulture) + "°"
                : null;

            HasAny = Orientation != 1 || HasGps || !string.IsNullOrEmpty(Make) || !string.IsNullOrEmpty(Model) ||
                     !string.IsNullOrEmpty(DateTimeOriginal) || !string.IsNullOrEmpty(Exposure) ||
                     !string.IsNullOrEmpty(Aperture) || !string.IsNullOrEmpty(Iso) ||
                     !string.IsNullOrEmpty(FocalLength) || !string.IsNullOrEmpty(LensModel) ||
                     !string.IsNullOrEmpty(Software) || !string.IsNullOrEmpty(Artist);
        }

        private bool hasLatSouth;
        private bool hasLonWest;

        internal void SetLatitude(double value, bool south)
        {
            GpsLatitude = value;
            hasLatSouth = south;
            HasGps = true;
        }

        internal void SetLongitude(double value, bool west)
        {
            GpsLongitude = value;
            hasLonWest = west;
            HasGps = true;
        }


        // ---------- 容器探测 ----------

        private static byte[] ReadHead(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int want = (int)Math.Min(fs.Length, MaxHeaderBytes);
                if (want < 16)
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

        /// <summary>返回 TIFF 头在文件中的偏移；找不到返回 -1。</summary>
        private static int FindTiffStart(byte[] d)
        {
            if (d.Length > 4 && d[0] == 0xFF && d[1] == 0xD8)
            {
                return FindJpegExif(d);
            }
            if (d.Length > 8 &&
                ((d[0] == 'I' && d[1] == 'I' && d[2] == 42 && d[3] == 0) ||
                 (d[0] == 'M' && d[1] == 'M' && d[2] == 0 && d[3] == 42)))
            {
                return 0;
            }
            if (d.Length > 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' &&
                d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
            {
                return FindWebpExif(d);
            }
            if (d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
            {
                return FindPngExif(d);
            }

            // HEIC/HEIF：EXIF 在 meta box 里，这里不做手工解析
            return -1;
        }

        private static int FindJpegExif(byte[] d)
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
                    return -1;
                }
                int segLen = (d[i + 2] << 8) | d[i + 3];
                if (segLen < 2)
                {
                    return -1;
                }
                if (marker == 0xE1 && i + 10 <= d.Length &&
                    d[i + 4] == 'E' && d[i + 5] == 'x' && d[i + 6] == 'i' && d[i + 7] == 'f' &&
                    d[i + 8] == 0 && d[i + 9] == 0)
                {
                    return i + 10;
                }
                i += 2 + segLen;
            }
            return -1;
        }

        /// <summary>WebP：RIFF 容器，EXIF 是一个 fourcc 块，内容可能是 "Exif\0\0" + TIFF，也可能直接是 TIFF。</summary>
        private static int FindWebpExif(byte[] d)
        {
            int pos = 12;
            while (pos + 8 <= d.Length)
            {
                string fourcc = Encoding.ASCII.GetString(d, pos, 4);
                int size = BitConverter.ToInt32(d, pos + 4);
                if (size < 0 || pos + 8 + size > d.Length)
                {
                    return -1;
                }
                if (fourcc == "EXIF")
                {
                    int start = pos + 8;
                    if (size > 6 && d[start] == 'E' && d[start + 1] == 'x' && d[start + 2] == 'i' &&
                        d[start + 3] == 'f' && d[start + 4] == 0 && d[start + 5] == 0)
                    {
                        return start + 6;
                    }
                    return start;
                }
                pos += 8 + size + (size & 1);
            }
            return -1;
        }

        /// <summary>PNG：eXIf 块的内容就是 TIFF 数据。</summary>
        private static int FindPngExif(byte[] d)
        {
            int pos = 8;
            while (pos + 8 <= d.Length)
            {
                int size = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
                if (size < 0 || pos + 12 + size > d.Length)
                {
                    return -1;
                }
                if (d[pos + 4] == 'e' && d[pos + 5] == 'X' && d[pos + 6] == 'I' && d[pos + 7] == 'f')
                {
                    return pos + 8;
                }
                pos += 12 + size;
            }
            return -1;
        }

        // ---------- TIFF / IFD ----------

        private sealed class TiffReader
        {
            private const int TypeByte = 1;
            private const int TypeAscii = 2;
            private const int TypeShort = 3;
            private const int TypeLong = 4;
            private const int TypeRational = 5;
            private const int TypeSByte = 6;
            private const int TypeSShort = 8;
            private const int TypeSLong = 9;
            private const int TypeSRational = 10;

            private const int TagExifIfd = 0x8769;
            private const int TagGpsIfd = 0x8825;

            private readonly byte[] _data;
            private readonly int _start;
            private readonly bool _little;
            private readonly ExifInfo _info;

            internal TiffReader(byte[] data, int start, ExifInfo info)
            {
                _data = data;
                _start = start;
                _little = data[start] == 'I';
                _info = info;
            }

            internal void Parse()
            {
                uint ifd0Offset = ReadU32(_start + 4);
                List<Entry> ifd0 = ReadIfd((int)(_start + ifd0Offset));

                _info.Make = Trim(ReadAscii(ifd0, 0x010F));
                _info.Model = Trim(ReadAscii(ifd0, 0x0110));
                _info.Software = Trim(ReadAscii(ifd0, 0x0131));
                _info.Artist = Trim(ReadAscii(ifd0, 0x013B));
                _info.Copyright = Trim(ReadAscii(ifd0, 0x8298));
                _info.DateTimeModified = FormatDate(ReadAscii(ifd0, 0x0132));

                int orientation = (int)ReadUInt(ifd0, 0x0112, 1);
                if (orientation >= 1 && orientation <= 8)
                {
                    _info.Orientation = orientation;
                }

                long exifOffset = ReadUInt(ifd0, TagExifIfd, 0);
                if (exifOffset > 0 && _start + exifOffset < _data.Length)
                {
                    List<Entry> exif = ReadIfd((int)(_start + exifOffset));
                    ParseExifIfd(exif);
                }

                long gpsOffset = ReadUInt(ifd0, TagGpsIfd, 0);
                if (gpsOffset > 0 && _start + gpsOffset < _data.Length)
                {
                    List<Entry> gps = ReadIfd((int)(_start + gpsOffset));
                    ParseGpsIfd(gps);
                }
            }

            private void ParseExifIfd(List<Entry> exif)
            {
                string taken = FormatDate(ReadAscii(exif, 0x9003));
                if (string.IsNullOrEmpty(taken))
                {
                    taken = FormatDate(ReadAscii(exif, 0x9004));
                }
                if (!string.IsNullOrEmpty(taken))
                {
                    _info.DateTimeOriginal = taken;
                }
                _info.DateTimeDigitized = FormatDate(ReadAscii(exif, 0x9004));

                double exposure = ReadRational(exif, 0x829A);
                if (exposure > 0)
                {
                    _info.Exposure = FormatExposure(exposure);
                }

                double fnumber = ReadRational(exif, 0x829D);
                if (fnumber > 0)
                {
                    _info.Aperture = "f/" + fnumber.ToString("0.#", CultureInfo.InvariantCulture);
                }

                long iso = ReadUInt(exif, 0x8827, 0);
                if (iso > 0)
                {
                    _info.Iso = "ISO " + iso.ToString(CultureInfo.InvariantCulture);
                }

                double focal = ReadRational(exif, 0x920A);
                if (focal > 0)
                {
                    _info.FocalLength = focal.ToString("0.#", CultureInfo.InvariantCulture) + " mm";
                }

                long focal35 = ReadUInt(exif, 0xA405, 0);
                if (focal35 > 0)
                {
                    _info.FocalLength35mm = "等效 " + focal35 + " mm";
                }

                double bias = ReadSignedRational(exif, 0x9204);
                if (Math.Abs(bias) > 0.0001)
                {
                    _info.ExposureBias = (bias > 0 ? "+" : string.Empty) +
                                         bias.ToString("0.#", CultureInfo.InvariantCulture) + " EV";
                }

                long program = ReadUInt(exif, 0x8822, 0);
                _info.ExposureProgram = DescribeExposureProgram(program);

                long metering = ReadUInt(exif, 0x9207, 0);
                _info.MeteringMode = DescribeMetering(metering);

                long flash = ReadUInt(exif, 0x9209, 0);
                _info.Flash = DescribeFlash(flash);

                long whiteBalance = ReadUInt(exif, 0xA403, 255);
                if (whiteBalance == 0)
                {
                    _info.WhiteBalance = "自动";
                }
                else if (whiteBalance == 1)
                {
                    _info.WhiteBalance = "手动";
                }

                long colorSpace = ReadUInt(exif, 0xA001, 0);
                if (colorSpace == 1)
                {
                    _info.ColorSpace = "sRGB";
                }
                else if (colorSpace == 2)
                {
                    _info.ColorSpace = "Adobe RGB";
                }
                else if (colorSpace == 0xFFFF)
                {
                    _info.ColorSpace = "未校准";
                }

                long scene = ReadUInt(exif, 0xA406, 255);
                _info.SceneCaptureType = DescribeScene(scene);

                long px = ReadUInt(exif, 0xA002, 0);
                long py = ReadUInt(exif, 0xA003, 0);
                if (px > 0 && px < int.MaxValue)
                {
                    _info.PixelWidth = (int)px;
                }
                if (py > 0 && py < int.MaxValue)
                {
                    _info.PixelHeight = (int)py;
                }

                _info.LensModel = Trim(ReadAscii(exif, 0xA434));
                if (string.IsNullOrEmpty(_info.LensModel))
                {
                    string lensMake = Trim(ReadAscii(exif, 0xA433));
                    if (!string.IsNullOrEmpty(lensMake))
                    {
                        _info.LensModel = lensMake;
                    }
                }
                double[] spec = ReadRationalArray(exif, 0xA432, 4);
                if (spec != null && spec[0] > 0 && spec[1] > 0)
                {
                    _info.LensSpecification = spec[0].ToString("0.#", CultureInfo.InvariantCulture) + "–" +
                                              spec[1].ToString("0.#", CultureInfo.InvariantCulture) + " mm";
                    if (spec[2] > 0)
                    {
                        _info.LensSpecification += " f/" + spec[2].ToString("0.#", CultureInfo.InvariantCulture);
                        if (spec[3] > spec[2])
                        {
                            _info.LensSpecification += "–" + spec[3].ToString("0.#", CultureInfo.InvariantCulture);
                        }
                    }
                }
            }

            private void ParseGpsIfd(List<Entry> gps)
            {
                string latRef = Trim(ReadAscii(gps, 0x0001));
                string lonRef = Trim(ReadAscii(gps, 0x0003));
                double[] lat = ReadRationalArray(gps, 0x0002, 3);
                double[] lon = ReadRationalArray(gps, 0x0004, 3);

                if (lat != null && lat.Length >= 3)
                {
                    double value = lat[0] + lat[1] / 60.0 + lat[2] / 3600.0;
                    _info.SetLatitude(value, string.Equals(latRef, "S", StringComparison.OrdinalIgnoreCase));
                }
                if (lon != null && lon.Length >= 3)
                {
                    double value = lon[0] + lon[1] / 60.0 + lon[2] / 3600.0;
                    _info.SetLongitude(value, string.Equals(lonRef, "W", StringComparison.OrdinalIgnoreCase));
                }

                double altitude = ReadRational(gps, 0x0006);
                if (altitude > 0)
                {
                    long below = ReadUInt(gps, 0x0005, 0);
                    double value = below == 1 ? -altitude : altitude;
                    _info.GpsAltitudeText = value.ToString("0.#", CultureInfo.InvariantCulture) + " m";
                }

                string date = Trim(ReadAscii(gps, 0x001D));
                double[] time = ReadRationalArray(gps, 0x0007, 3);
                if (!string.IsNullOrEmpty(date) && time != null && time.Length >= 3)
                {
                    _info.GpsTimeText = date.Replace(':', '-') + " " +
                                        ((int)time[0]).ToString("00") + ":" +
                                        ((int)time[1]).ToString("00") + ":" +
                                        ((int)time[2]).ToString("00") + " UTC";
                }
            }

            private struct Entry
            {
                public ushort Tag;
                public ushort Type;
                public uint Count;
                public int ValueOffset;   // 外置值时：相对 _data 的绝对偏移
                public int ValueField;    // 12 字节条目里 4 字节值域的起始位置
                public int ValueSize;     // 单个值的字节数
                public bool Inline;
            }

            private List<Entry> ReadIfd(int offset)
            {
                List<Entry> list = new List<Entry>();
                if (offset < 0 || offset + 2 > _data.Length)
                {
                    return list;
                }
                int count = ReadU16(offset);
                if (count <= 0 || count > 4096)
                {
                    return list;
                }
                int pos = offset + 2;
                for (int i = 0; i < count; i++)
                {
                    if (pos + 12 > _data.Length)
                    {
                        break;
                    }
                    Entry e = new Entry();
                    e.Tag = (ushort)ReadU16(pos);
                    e.Type = (ushort)ReadU16(pos + 2);
                    e.Count = ReadU32(pos + 4);
                    e.ValueSize = TypeSize(e.Type);
                    int total = e.ValueSize * (int)Math.Min(e.Count, 4096u);
                    e.ValueField = pos + 8;
                    if (total <= 4)
                    {
                        e.Inline = true;
                        e.ValueOffset = e.ValueField;
                    }
                    else
                    {
                        e.Inline = false;
                        e.ValueOffset = (int)(_start + ReadU32(pos + 8));
                    }
                    list.Add(e);
                    pos += 12;
                }
                return list;
            }

            private static int TypeSize(int type)
            {
                switch (type)
                {
                    case TypeByte:
                    case TypeAscii:
                    case TypeSByte:
                        return 1;
                    case TypeShort:
                    case TypeSShort:
                        return 2;
                    case TypeLong:
                    case TypeSLong:
                        return 4;
                    case TypeRational:
                    case TypeSRational:
                        return 8;
                    default:
                        return 1;
                }
            }

            /// <summary>少于 4 字节的值按 TIFF 规范左对齐存放在值域里，与字节序无关。</summary>
            private int ValueAddress(Entry e)
            {
                return e.Inline ? e.ValueField : e.ValueOffset;
            }

            private string ReadAscii(List<Entry> entries, int tag)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (e.Tag != tag || (e.Type != TypeAscii && e.Type != TypeByte) || e.Count == 0)
                    {
                        continue;
                    }
                    int address = ValueAddress(e);
                    int count = (int)Math.Min(e.Count, 512u);
                    if (address < 0 || address + count > _data.Length)
                    {
                        return null;
                    }
                    StringBuilder sb = new StringBuilder(count);
                    for (int k = 0; k < count; k++)
                    {
                        byte b = _data[address + k];
                        if (b == 0)
                        {
                            break;
                        }
                        sb.Append((char)b);
                    }
                    return sb.ToString();
                }
                return null;
            }

            private uint ReadUInt(List<Entry> entries, int tag, uint fallback)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (e.Tag != tag)
                    {
                        continue;
                    }
                    int address = ValueAddress(e);
                    if (e.Type == TypeByte)
                    {
                        return address < _data.Length ? _data[address] : fallback;
                    }
                    if (e.Type == TypeShort)
                    {
                        return (uint)ReadU16(address);
                    }
                    if (e.Type == TypeLong)
                    {
                        return ReadU32(address);
                    }
                }
                return fallback;
            }

            private double ReadRational(List<Entry> entries, int tag)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (e.Tag != tag || e.Type != TypeRational)
                    {
                        continue;
                    }
                    int address = ValueAddress(e);
                    uint num = ReadU32(address);
                    uint den = ReadU32(address + 4);
                    if (den == 0)
                    {
                        return 0;
                    }
                    return (double)num / den;
                }
                return 0;
            }

            private double ReadSignedRational(List<Entry> entries, int tag)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (e.Tag != tag || e.Type != TypeSRational)
                    {
                        continue;
                    }
                    int address = ValueAddress(e);
                    int num = (int)ReadU32(address);
                    int den = (int)ReadU32(address + 4);
                    if (den == 0)
                    {
                        return 0;
                    }
                    return (double)num / den;
                }
                return 0;
            }

            private double[] ReadRationalArray(List<Entry> entries, int tag, int expected)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    if (e.Tag != tag || e.Type != TypeRational || e.Count == 0)
                    {
                        continue;
                    }
                    int count = Math.Min((int)e.Count, expected);
                    int address = ValueAddress(e);
                    double[] result = new double[count];
                    for (int k = 0; k < count; k++)
                    {
                        uint num = ReadU32(address + k * 8);
                        uint den = ReadU32(address + k * 8 + 4);
                        result[k] = den == 0 ? 0 : (double)num / den;
                    }
                    return result;
                }
                return null;
            }

            private int ReadU16(int offset)
            {
                if (offset < 0 || offset + 2 > _data.Length)
                {
                    return 0;
                }
                return _little
                    ? (_data[offset] | (_data[offset + 1] << 8))
                    : ((_data[offset] << 8) | _data[offset + 1]);
            }

            private uint ReadU32(int offset)
            {
                if (offset < 0 || offset + 4 > _data.Length)
                {
                    return 0;
                }
                if (_little)
                {
                    return (uint)(_data[offset] | (_data[offset + 1] << 8) | (_data[offset + 2] << 16) |
                                  (_data[offset + 3] << 24));
                }
                return (uint)((_data[offset] << 24) | (_data[offset + 1] << 16) | (_data[offset + 2] << 8) |
                              _data[offset + 3]);
            }

            // ---------- 文案 ----------

            private static string Trim(string s)
            {
                return s == null ? null : s.Trim().TrimEnd('\0').Trim();
            }

            private static string FormatDate(string raw)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    return null;
                }
                string s = raw.Trim();
                if (s.Length < 16)
                {
                    return s;
                }
                return s.Substring(0, 4) + "-" + s.Substring(5, 2) + "-" + s.Substring(8, 2) + " " +
                       s.Substring(11, 5);
            }

            private static string FormatExposure(double seconds)
            {
                if (seconds <= 0)
                {
                    return null;
                }
                if (seconds >= 1)
                {
                    return seconds.ToString("0.#", CultureInfo.InvariantCulture) + " 秒";
                }
                int den = (int)Math.Round(1.0 / seconds);
                if (den <= 0)
                {
                    den = 1;
                }
                return "1/" + den + " 秒";
            }

            private static string DescribeExposureProgram(long value)
            {
                switch (value)
                {
                    case 1:
                        return "手动";
                    case 2:
                        return "程序自动";
                    case 3:
                        return "光圈优先";
                    case 4:
                        return "快门优先";
                    case 5:
                        return "创意程序";
                    case 6:
                        return "运动";
                    case 7:
                        return "人像";
                    case 8:
                        return "风景";
                    default:
                        return null;
                }
            }

            private static string DescribeMetering(long value)
            {
                switch (value)
                {
                    case 1:
                        return "平均";
                    case 2:
                        return "中央重点";
                    case 3:
                        return "点测光";
                    case 4:
                        return "多点";
                    case 5:
                        return "图案（矩阵）";
                    case 6:
                        return "局部";
                    default:
                        return null;
                }
            }

            private static string DescribeFlash(long value)
            {
                bool fired = (value & 0x1) != 0;
                long mode = (value >> 3) & 0x3;
                string modeText = null;
                if (mode == 1)
                {
                    modeText = "强制";
                }
                else if (mode == 2)
                {
                    modeText = "强制关闭";
                }
                else if (mode == 3)
                {
                    modeText = "自动";
                }

                if (modeText == null)
                {
                    return fired ? "已闪光" : "未闪光";
                }
                return modeText + " · " + (fired ? "已闪光" : "未闪光");
            }

            private static string DescribeScene(long value)
            {
                switch (value)
                {
                    case 0:
                        return "标准";
                    case 1:
                        return "风景";
                    case 2:
                        return "人像";
                    case 3:
                        return "夜景";
                    case 4:
                        return "夜景人像";
                    default:
                        return null;
                }
            }
        }
    }
}
