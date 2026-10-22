using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ImageView.Core
{
    /// <summary>
    /// 图片缩放绘制走 GDI 的 StretchBlt(HALFTONE)。
    ///
    /// 为什么不用 GDI+：实测同一张 1600×1200 → 1116×740，
    ///   GDI+ 双线性 = 31.6ms，双三次 = 52ms，srcRect 重载 = 50ms，
    ///   GDI StretchBlt HALFTONE = 3.2ms（COLORONCOLOR 0.7ms）。
    /// 差 10 倍以上，直接决定拖拽是否跟手。
    ///
    /// 限制：StretchBlt 不支持逐像素 alpha，所以带透明的图仍走 GDI+ 路径。
    /// </summary>
    internal static class GdiBlitter
    {
        private const int Halftone = 4;
        private const uint SrcCopy = 0x00CC0020;

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int widthDest, int heightDest,
            IntPtr hdcSrc, int xSrc, int ySrc, int widthSrc, int heightSrc, uint rop);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr previous);

        private static Bitmap _cachedSource;
        private static IntPtr _hBitmap = IntPtr.Zero;
        private static IntPtr _memoryDc = IntPtr.Zero;
        private static IntPtr _previousObject = IntPtr.Zero;
        private static readonly object Gate = new object();

        /// <summary>绘制一帧。失败或不可用时返回 false，由调用方退回 GDI+。</summary>
        internal static bool TryDraw(Graphics g, Bitmap source, Rectangle dest, Rectangle src)
        {
            if (source == null || g == null || dest.Width <= 0 || dest.Height <= 0 ||
                src.Width <= 0 || src.Height <= 0)
            {
                return false;
            }
            lock (Gate)
            {
                try
                {
                    if (!EnsureSource(source))
                    {
                        return false;
                    }
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        SetStretchBltMode(hdc, Halftone);
                        SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                        return StretchBlt(hdc, dest.X, dest.Y, dest.Width, dest.Height,
                            _memoryDc, src.X, src.Y, src.Width, src.Height, SrcCopy);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("GdiBlitter.TryDraw", ex);
                    ReleaseSource();
                    return false;
                }
            }
        }

        private static bool EnsureSource(Bitmap source)
        {
            if (ReferenceEquals(_cachedSource, source) && _memoryDc != IntPtr.Zero)
            {
                return true;
            }
            ReleaseSource();
            try
            {
                _hBitmap = source.GetHbitmap(Color.FromArgb(0));
                _memoryDc = CreateCompatibleDC(IntPtr.Zero);
                if (_memoryDc == IntPtr.Zero)
                {
                    return false;
                }
                _previousObject = SelectObject(_memoryDc, _hBitmap);
                _cachedSource = source;
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("GdiBlitter.EnsureSource", ex);
                ReleaseSource();
                return false;
            }
        }

        internal static void ReleaseSource()
        {
            try
            {
                if (_memoryDc != IntPtr.Zero)
                {
                    if (_previousObject != IntPtr.Zero)
                    {
                        SelectObject(_memoryDc, _previousObject);
                    }
                    DeleteDC(_memoryDc);
                }
                if (_hBitmap != IntPtr.Zero)
                {
                    DeleteObject(_hBitmap);
                }
            }
            catch (Exception)
            {
            }
            _memoryDc = IntPtr.Zero;
            _hBitmap = IntPtr.Zero;
            _previousObject = IntPtr.Zero;
            _cachedSource = null;
        }
    }
}
