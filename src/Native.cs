using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ImageView
{
    /// <summary>集中所有 Win32 互操作声明，其他文件不直接写 DllImport。</summary>
    internal static class Native
    {
        // ---- 窗口消息 ----
        internal const int WM_SETTINGCHANGE = 0x001A;
        internal const int WM_GETMINMAXINFO = 0x0024;
        internal const int WM_DPICHANGED = 0x02E0;
        internal const int WM_NCCALCSIZE = 0x0083;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_NCACTIVATE = 0x0086;
        internal const int WM_SYSCOMMAND = 0x0112;
        internal const int WM_ERASEBKGND = 0x0014;

        // ---- 命中测试 ----
        internal const int HTCLIENT = 1;
        internal const int HTCAPTION = 2;
        internal const int HTLEFT = 10;
        internal const int HTRIGHT = 11;
        internal const int HTTOP = 12;
        internal const int HTTOPLEFT = 13;
        internal const int HTTOPRIGHT = 14;
        internal const int HTBOTTOM = 15;
        internal const int HTBOTTOMLEFT = 16;
        internal const int HTBOTTOMRIGHT = 17;

        // ---- DWM ----
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_NEW = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        // ---- SystemParametersInfo ----
        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        // ---- Shell ----
        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_WANTNUKEWARNING = 0x4000;
        internal const int SHCNE_ASSOCCHANGED = 0x08000000;
        internal const uint SHCNF_IDLIST = 0x0000;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            internal int Width { get { return Right - Left; } }
            internal int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NCCALCSIZE_PARAMS
        {
            public RECT Rc0;
            public RECT Rc1;
            public RECT Rc2;
            public IntPtr Lppos;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MINMAXINFO
        {
            public POINT Reserved;
            public POINT MaxSize;
            public POINT MaxPosition;
            public POINT MinTrackSize;
            public POINT MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        internal static extern bool IsZoomed(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        [DllImport("shell32.dll")]
        internal static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        /// <summary>暗色标题栏（Win10 20H1+ 用属性 20，早期版本用 19）。返回是否成功。</summary>
        internal static bool SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            int value = dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_NEW, ref value, sizeof(int)) == 0)
                {
                    return true;
                }
                return DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int)) == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        /// <summary>Win11 圆角；Win10 上调用失败，忽略即可。</summary>
        internal static void TryRoundCorners(IntPtr hwnd)
        {
            int pref = 2; // DWMWCP_ROUND
            try
            {
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch (Exception)
            {
                // 老系统没有该属性，忽略
            }
        }

        internal static bool SetWallpaper(string path)
        {
            try
            {
                return SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>删除到回收站。成功返回 0。</summary>
        internal static int MoveToRecycleBin(IntPtr owner, string path)
        {
            SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
            op.hwnd = owner;
            op.wFunc = FO_DELETE;
            op.pFrom = path + "\0";       // 需要双 null 结尾
            op.pTo = null;
            op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT);
            try
            {
                return SHFileOperation(ref op);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        internal static bool TryAttachParentConsole()
        {
            try
            {
                if (AttachConsole(-1))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        internal static bool TryAllocConsole()
        {
            try
            {
                return AllocConsole();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>目标显示器的工作区（用于窗口位置合法性校验）。</summary>
        internal static Rectangle WorkAreaFor(IntPtr hwnd)
        {
            try
            {
                IntPtr mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
                MONITORINFO info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(mon, ref info))
                {
                    return Rectangle.FromLTRB(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
                }
            }
            catch (Exception)
            {
            }
            return SystemInformation.WorkingArea;
        }
    }
}
