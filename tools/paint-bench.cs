using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

/// <summary>
/// 贴图性能对比：找出“高质量 + 快”的缩放绘制路径。
/// 结论会直接决定画布用哪套渲染策略。
/// 编译: csc /target:exe /platform:x64 /r:System.Drawing.dll /out:tools\paint-bench.exe tools\paint-bench.cs
/// </summary>
internal static class PaintBench
{
    private const int Iterations = 20;

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    private const int HALFTONE = 4;
    private const int SRCCOPY = 0x00CC0020;

    private static void Main()
    {
        Console.WriteLine("目标画布 1116x740；源位图 1600x1200（与预览档同尺寸）");
        Console.WriteLine();

        foreach (PixelFormat format in new PixelFormat[]
                 {
                     PixelFormat.Format32bppPArgb, PixelFormat.Format32bppArgb, PixelFormat.Format24bppRgb
                 })
        {
            using (Bitmap source = new Bitmap(1600, 1200, format))
            using (Bitmap target = new Bitmap(1200, 800, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(source))
                {
                    g.Clear(Color.DarkSlateBlue);
                    g.FillEllipse(Brushes.Orange, 200, 200, 900, 700);
                }
                Console.WriteLine("源格式 {0}", format);
                FullImage("  整图→933x700  Bilinear ", source, target, new Rectangle(0, 0, 933, 700), InterpolationMode.Bilinear);
                FullImage("  整图→933x700  HQBicubic", source, target, new Rectangle(0, 0, 933, 700), InterpolationMode.HighQualityBicubic);
                FullImage("  整图→1116x740 Bilinear ", source, target, new Rectangle(0, 0, 1116, 740), InterpolationMode.Bilinear);
                SourceRect("  srcRect→1116x740 Bilin  ", source, target, new Rectangle(0, 0, 1116, 740), new RectangleF(520, 380, 558, 370), InterpolationMode.Bilinear);
                SourceRect("  srcRect→1116x740 HQBic  ", source, target, new Rectangle(0, 0, 1116, 740), new RectangleF(520, 380, 558, 370), InterpolationMode.HighQualityBicubic);
                Halftone("  StretchBlt HALFTONE 2x  ", source, target, new Rectangle(0, 0, 1116, 740), new Rectangle(520, 380, 558, 370));
                Halftone("  StretchBlt COLORONCOLOR  ", source, target, new Rectangle(0, 0, 1116, 740), new Rectangle(520, 380, 558, 370), 3);
                Console.WriteLine();
            }
        }
    }

    private static void FullImage(string label, Bitmap source, Bitmap target, Rectangle dest, InterpolationMode mode)
    {
        using (Graphics g = Graphics.FromImage(target))
        {
            g.InterpolationMode = mode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(source, dest);
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                g.DrawImage(source, dest);
            }
            Console.WriteLine("{0} {1,7:0.00} ms", label, sw.Elapsed.TotalMilliseconds / Iterations);
        }
    }

    private static void SourceRect(string label, Bitmap source, Bitmap target, Rectangle dest, RectangleF src,
        InterpolationMode mode)
    {
        using (Graphics g = Graphics.FromImage(target))
        {
            g.InterpolationMode = mode;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(source, dest, src, GraphicsUnit.Pixel);
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                g.DrawImage(source, dest, src, GraphicsUnit.Pixel);
            }
            Console.WriteLine("{0} {1,7:0.00} ms", label, sw.Elapsed.TotalMilliseconds / Iterations);
        }
    }

    private static void Halftone(string label, Bitmap source, Bitmap target, Rectangle dest, Rectangle src,
        int mode = HALFTONE)
    {
        IntPtr sourceHdc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            using (Graphics tg = Graphics.FromImage(target))
            {
                sourceHdc = tg.GetHdc();
                memoryDc = CreateCompatibleDC(sourceHdc);
                hBitmap = source.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memoryDc, hBitmap);
                SetStretchBltMode(sourceHdc, mode);

                StretchBlt(sourceHdc, dest.X, dest.Y, dest.Width, dest.Height, memoryDc, src.X, src.Y, src.Width,
                    src.Height, SRCCOPY);
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < Iterations; i++)
                {
                    StretchBlt(sourceHdc, dest.X, dest.Y, dest.Width, dest.Height, memoryDc, src.X, src.Y, src.Width,
                        src.Height, SRCCOPY);
                }
                sw.Stop();
                Console.WriteLine("{0} {1,7:0.00} ms", label, sw.Elapsed.TotalMilliseconds / Iterations);
                tg.ReleaseHdc(sourceHdc);
                sourceHdc = IntPtr.Zero;
            }
        }
        finally
        {
            if (sourceHdc != IntPtr.Zero) { }
            if (previous != IntPtr.Zero) { SelectObject(memoryDc, previous); }
            if (hBitmap != IntPtr.Zero) { DeleteObject(hBitmap); }
            if (memoryDc != IntPtr.Zero) { DeleteDC(memoryDc); }
        }
    }
}
