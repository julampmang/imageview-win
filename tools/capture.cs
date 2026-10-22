using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

/// <summary>
/// 窗口截图小工具：把指定句柄的窗口用 PrintWindow 抓成 PNG。
/// 用法: capture.exe <hwnd> <输出路径>
/// 单独编译（不进主程序）:
///   csc /target:exe /r:System.Drawing.dll /out:tools\capture.exe tools\capture.cs
/// </summary>
internal static class Capture
{
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: capture.exe <hwnd> <out.png>");
            return 2;
        }

        IntPtr hwnd = new IntPtr(long.Parse(args[0]));
        RECT rect;
        if (!GetWindowRect(hwnd, out rect))
        {
            Console.Error.WriteLine("GetWindowRect failed");
            return 3;
        }

        int w = Math.Max(1, rect.Right - rect.Left);
        int h = Math.Max(1, rect.Bottom - rect.Top);

        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(args[1], ImageFormat.Png);
        }

        Console.WriteLine(w + "x" + h);
        return 0;
    }
}
