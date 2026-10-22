using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageView.Core
{
    /// <summary>
    /// WIC 解码。相比 GDI+ 的关键优势：
    ///   1) 支持系统装了编解码器的所有格式（WebP / HEIC / AVIF …）；
    ///   2) 能在解码时直接缩放，不必先把 8000×6000 解成全尺寸（省内存、快一个数量级）。
    /// 所有 COM 调用都包在 try 里，失败时上层会退回 GDI+。
    /// </summary>
    internal static class WicDecoder
    {
        private static readonly Guid ClsidImagingFactory = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");
        private static readonly Guid PixelFormat32bppPBGRA = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc910");

        private const uint GenericRead = 0x80000000;
        private const int DecodeMetadataCacheOnDemand = 0;

        private const int InterpolationFant = 3;
        private const int InterpolationLinear = 1;
        private const int DitherNone = 0;
        private const int PaletteTypeCustom = 0;

        private static IWICImagingFactory _factory;
        private static bool _factoryTried;
        private static bool _available;
        private static string _lastError;

        internal static bool Available
        {
            get
            {
                Factory();
                return _available;
            }
        }

        internal static string LastError
        {
            get { return _lastError; }
        }

        private static IWICImagingFactory Factory()
        {
            if (_factoryTried)
            {
                return _factory;
            }
            _factoryTried = true;
            try
            {
                Type type = Type.GetTypeFromCLSID(ClsidImagingFactory, true);
                object instance = Activator.CreateInstance(type);
                _factory = (IWICImagingFactory)instance;
                _available = _factory != null;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _factory = null;
                _available = false;
            }
            return _factory;
        }

        /// <summary>只读尺寸与帧数，不做像素解码。</summary>
        internal static bool TryProbe(string path, out int width, out int height, out int frameCount)
        {
            width = 0;
            height = 0;
            frameCount = 1;
            IWICImagingFactory factory = Factory();
            if (factory == null)
            {
                return false;
            }
            IWICBitmapDecoder decoder = null;
            IWICBitmapFrameDecode frame = null;
            try
            {
                decoder = Open(factory, path);
                if (decoder == null)
                {
                    return false;
                }
                uint frames;
                decoder.GetFrameCount(out frames);
                frameCount = frames < 1 ? 1 : (int)frames;

                if (decoder.GetFrame(0, out frame) != 0 || frame == null)
                {
                    return false;
                }
                uint w, h;
                if (frame.GetSize(out w, out h) != 0)
                {
                    return false;
                }
                width = (int)w;
                height = (int)h;
                return width > 0 && height > 0;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
            finally
            {
                ReleaseCom(frame);
                ReleaseCom(decoder);
            }
        }

        /// <summary>
        /// 解码到“最长边不超过 maxSide”——WIC 在解码管线里完成缩放。
        /// maxSide &lt;= 0 表示按原始尺寸解码。
        /// </summary>
        internal static Bitmap Decode(string path, int maxSide, out int sourceWidth, out int sourceHeight,
            out bool hasAlpha)
        {
            sourceWidth = 0;
            sourceHeight = 0;
            hasAlpha = false;

            IWICImagingFactory factory = Factory();
            if (factory == null)
            {
                return null;
            }

            IWICBitmapDecoder decoder = null;
            IWICBitmapFrameDecode frame = null;
            IWICBitmapScaler scaler = null;
            try
            {
                decoder = Open(factory, path);
                if (decoder == null)
                {
                    return null;
                }
                if (decoder.GetFrame(0, out frame) != 0 || frame == null)
                {
                    return null;
                }

                uint sw, sh;
                if (frame.GetSize(out sw, out sh) != 0 || sw == 0 || sh == 0)
                {
                    return null;
                }
                sourceWidth = (int)sw;
                sourceHeight = (int)sh;

                int targetMax = maxSide;
                if (targetMax > 0 && Math.Max(sourceWidth, sourceHeight) > targetMax)
                {
                    double scale = (double)targetMax / Math.Max(sourceWidth, sourceHeight);
                    int tw = Math.Max(1, (int)Math.Round(sourceWidth * scale));
                    int th = Math.Max(1, (int)Math.Round(sourceHeight * scale));

                    if (factory.CreateBitmapScaler(out scaler) == 0 && scaler != null)
                    {
                        int hr = scaler.Initialize(frame, (uint)tw, (uint)th, InterpolationFant);
                        if (hr != 0)
                        {
                            hr = scaler.Initialize(frame, (uint)tw, (uint)th, InterpolationLinear);
                        }
                        if (hr == 0)
                        {
                            Bitmap scaled = ConvertToBitmap(factory, (IWICBitmapSource)scaler, out hasAlpha);
                            if (scaled != null)
                            {
                                return scaled;
                            }
                        }
                    }
                }

                return ConvertToBitmap(factory, (IWICBitmapSource)frame, out hasAlpha);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return null;
            }
            finally
            {
                // WIC 的帧解码器会缓存解压后的整幅图，不显式释放会让内存高高挂着
                ReleaseCom(scaler);
                ReleaseCom(frame);
                ReleaseCom(decoder);
            }
        }

        private static void ReleaseCom(object comObject)
        {
            if (comObject == null)
            {
                return;
            }
            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch (Exception)
            {
            }
        }

        private static IWICBitmapDecoder Open(IWICImagingFactory factory, string path)
        {
            try
            {
                IWICBitmapDecoder decoder;
                int hr = factory.CreateDecoderFromFilename(path, IntPtr.Zero, GenericRead,
                    DecodeMetadataCacheOnDemand, out decoder);
                if (hr != 0)
                {
                    _lastError = "CreateDecoderFromFilename hr=0x" + hr.ToString("X8");
                    return null;
                }
                return decoder;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return null;
            }
        }

        private static Bitmap ConvertToBitmap(IWICImagingFactory factory, IWICBitmapSource source, out bool hasAlpha)
        {
            hasAlpha = false;
            IWICFormatConverter converter = null;
            try
            {
                if (factory.CreateFormatConverter(out converter) != 0 || converter == null)
                {
                    return null;
                }
                Guid format = PixelFormat32bppPBGRA;
                int hr = converter.Initialize(source, ref format, DitherNone, IntPtr.Zero, 0.0, PaletteTypeCustom);
                if (hr != 0)
                {
                    _lastError = "FormatConverter.Initialize hr=0x" + hr.ToString("X8");
                    return null;
                }

                uint w, h;
                if (converter.GetSize(out w, out h) != 0 || w == 0 || h == 0)
                {
                    return null;
                }
                int width = (int)w;
                int height = (int)h;
                int stride = width * 4;
                int size = stride * height;

                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    hr = converter.CopyPixels(IntPtr.Zero, (uint)stride, (uint)size, buffer);
                    if (hr != 0)
                    {
                        _lastError = "CopyPixels hr=0x" + hr.ToString("X8");
                        return null;
                    }

                    hasAlpha = ScanAlpha(buffer, width, height);

                    Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                    BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppPArgb);
                    try
                    {
                        // 直接从非托管缓冲逐行拷进位图：避免再开一份几十 MB 的托管数组
                        int rowBytes = Math.Min(stride, Math.Abs(bits.Stride));
                        for (int y = 0; y < height; y++)
                        {
                            CopyMemory(new IntPtr(bits.Scan0.ToInt64() + (long)y * bits.Stride),
                                new IntPtr(buffer.ToInt64() + (long)y * stride), (UIntPtr)(uint)rowBytes);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(bits);
                    }
                    return bitmap;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return null;
            }
            finally
            {
                if (converter != null)
                {
                    try
                    {
                        Marshal.FinalReleaseComObject(converter);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        /// <summary>PBGRA 下 alpha 在每像素第 4 字节；扫描是否有真实透明。</summary>
        private static bool ScanAlpha(IntPtr buffer, int width, int height)
        {
            int stride = width * 4;
            // 大图没必要全扫：采样 64 行即可判定
            int step = Math.Max(1, height / 64);
            byte[] row = new byte[stride];
            for (int y = 0; y < height; y += step)
            {
                Marshal.Copy(new IntPtr(buffer.ToInt64() + (long)y * stride), row, 0, stride);
                for (int x = 3; x < stride; x += 4)
                {
                    if (row[x] != 0xFF)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);

        // ------------------------------------------------------------------
        // COM 互操作：只声明用得到的方法，但 vtable 顺序必须与 wincodec.h 完全一致。
        // ------------------------------------------------------------------

        [ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICImagingFactory
        {
            [PreserveSig]
            int CreateDecoderFromFilename([MarshalAs(UnmanagedType.LPWStr)] string filename, IntPtr vendor,
                uint desiredAccess, int metadataOptions, out IWICBitmapDecoder decoder);

            [PreserveSig]
            int CreateDecoderFromStream(IntPtr stream, IntPtr vendor, int metadataOptions,
                out IWICBitmapDecoder decoder);

            [PreserveSig]
            int CreateDecoderFromFileHandle(IntPtr fileHandle, IntPtr vendor, int metadataOptions,
                out IWICBitmapDecoder decoder);

            [PreserveSig]
            int CreateComponentInfo(ref Guid clsid, out IntPtr info);

            [PreserveSig]
            int CreateDecoder(ref Guid containerFormat, IntPtr vendor, out IWICBitmapDecoder decoder);

            [PreserveSig]
            int CreateEncoder(ref Guid containerFormat, IntPtr vendor, out IntPtr encoder);

            [PreserveSig]
            int CreatePalette(out IntPtr palette);

            [PreserveSig]
            int CreateFormatConverter(out IWICFormatConverter converter);

            [PreserveSig]
            int CreateBitmapScaler(out IWICBitmapScaler scaler);

            [PreserveSig]
            int CreateBitmapClipper(out IntPtr clipper);

            [PreserveSig]
            int CreateBitmapFlipRotator(out IntPtr rotator);

            [PreserveSig]
            int CreateStream(out IntPtr stream);

            [PreserveSig]
            int CreateColorContext(out IntPtr colorContext);

            [PreserveSig]
            int CreateColorTransformer(out IntPtr colorTransformer);

            [PreserveSig]
            int CreateBitmap(uint width, uint height, ref Guid pixelFormat, int option, out IntPtr bitmap);

            [PreserveSig]
            int CreateBitmapFromSource(IntPtr source, int option, out IntPtr bitmap);

            [PreserveSig]
            int CreateBitmapFromSourceRect(IntPtr source, uint x, uint y, uint width, uint height,
                out IntPtr bitmap);

            [PreserveSig]
            int CreateBitmapFromMemory(uint width, uint height, ref Guid pixelFormat, uint stride, uint bufferSize,
                IntPtr buffer, out IntPtr bitmap);

            [PreserveSig]
            int CreateBitmapFromHBITMAP(IntPtr hBitmap, IntPtr hPalette, int option, out IntPtr bitmap);

            [PreserveSig]
            int CreateBitmapFromHICON(IntPtr hIcon, out IntPtr bitmap);

            [PreserveSig]
            int CreateComponentEnumerator(int componentTypes, int options, out IntPtr enumerator);
        }

        [ComImport, Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapDecoder
        {
            [PreserveSig]
            int QueryCapability(IntPtr stream, out int capability);

            [PreserveSig]
            int Initialize(IntPtr stream, int cacheOptions);

            [PreserveSig]
            int GetContainerFormat(out Guid containerFormat);

            [PreserveSig]
            int GetDecoderInfo(out IntPtr info);

            [PreserveSig]
            int CopyPalette(IntPtr palette);

            [PreserveSig]
            int GetMetadataQueryReader(out IntPtr reader);

            [PreserveSig]
            int GetPreview(out IntPtr preview);

            [PreserveSig]
            int GetColorContexts(uint count, IntPtr colorContexts, out uint actualCount);

            [PreserveSig]
            int GetThumbnail(out IntPtr thumbnail);

            [PreserveSig]
            int GetFrameCount(out uint count);

            [PreserveSig]
            int GetFrame(uint index, out IWICBitmapFrameDecode frame);
        }

        [ComImport, Guid("3b16811b-6a43-4ec9-a813-3d930c13b940"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapFrameDecode
        {
            [PreserveSig]
            int GetSize(out uint width, out uint height);

            [PreserveSig]
            int GetPixelFormat(out Guid pixelFormat);

            [PreserveSig]
            int GetResolution(out double dpiX, out double dpiY);

            [PreserveSig]
            int CopyPalette(IntPtr palette);

            [PreserveSig]
            int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);

            [PreserveSig]
            int GetMetadataQueryReader(out IntPtr reader);

            [PreserveSig]
            int GetColorContexts(uint count, IntPtr colorContexts, out uint actualCount);

            [PreserveSig]
            int GetThumbnail(out IntPtr thumbnail);
        }

        [ComImport, Guid("00000302-a8f2-4877-ba0a-fd2b6645fb94"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapScaler
        {
            [PreserveSig]
            int GetSize(out uint width, out uint height);

            [PreserveSig]
            int GetPixelFormat(out Guid pixelFormat);

            [PreserveSig]
            int GetResolution(out double dpiX, out double dpiY);

            [PreserveSig]
            int CopyPalette(IntPtr palette);

            [PreserveSig]
            int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);

            [PreserveSig]
            int Initialize(IWICBitmapFrameDecode source, uint width, uint height, int mode);
        }

        [ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICFormatConverter
        {
            [PreserveSig]
            int GetSize(out uint width, out uint height);

            [PreserveSig]
            int GetPixelFormat(out Guid pixelFormat);

            [PreserveSig]
            int GetResolution(out double dpiX, out double dpiY);

            [PreserveSig]
            int CopyPalette(IntPtr palette);

            [PreserveSig]
            int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);

            [PreserveSig]
            int Initialize(IWICBitmapSource source, ref Guid dstFormat, int dither, IntPtr palette,
                double alphaThresholdPercent, int paletteTranslate);
        }

        /// <summary>仅用于把 scaler 传给格式转换器（两者都实现 IWICBitmapSource）。</summary>
        [ComImport, Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapSource
        {
            [PreserveSig]
            int GetSize(out uint width, out uint height);

            [PreserveSig]
            int GetPixelFormat(out Guid pixelFormat);

            [PreserveSig]
            int GetResolution(out double dpiX, out double dpiY);

            [PreserveSig]
            int CopyPalette(IntPtr palette);

            [PreserveSig]
            int CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
        }
    }
}
