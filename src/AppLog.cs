using System;
using System.IO;
using System.Text;

namespace ImageView
{
    /// <summary>
    /// 轻量日志：命令行模式下同时输出到控制台，错误始终落盘到数据目录。
    /// 绝不因为日志本身抛异常。
    /// </summary>
    internal static class AppLog
    {
        private static readonly StringBuilder Buffer = new StringBuilder();
        private static bool _consoleReady;
        private static bool _consoleChecked;

        internal static string DataDir
        {
            get { return AppPaths.DataDir; }
        }

        /// <summary>selftest/bench 时尝试挂到父控制台，让输出能被我直接看到。</summary>
        internal static void EnableConsole()
        {
            if (_consoleChecked)
            {
                return;
            }
            _consoleChecked = true;
            try
            {
                // 只在父进程已有控制台时挂上去；不新开控制台窗口，避免弹窗骚扰
                if (Native.TryAttachParentConsole())
                {
                    _consoleReady = true;
                }
            }
            catch (Exception)
            {
                _consoleReady = false;
            }
        }

        internal static void Line(string message)
        {
            string text = message ?? string.Empty;
            Buffer.AppendLine(text);
            if (_consoleReady)
            {
                try
                {
                    Console.WriteLine(text);
                    Console.Out.Flush();
                }
                catch (Exception)
                {
                }
            }
        }

        internal static void Line(string format, params object[] args)
        {
            Line(string.Format(format, args));
        }

        internal static void Error(string context, Exception ex)
        {
            string text = string.Format("[{0}] {1}: {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), context,
                ex == null ? "(null)" : ex.ToString());
            Buffer.AppendLine(text);
            if (_consoleReady)
            {
                try
                {
                    Console.Error.WriteLine(text);
                }
                catch (Exception)
                {
                }
            }
            try
            {
                EnsureDir();
                using (StreamWriter w = new StreamWriter(Path.Combine(DataDir, "error.log"), true, Encoding.UTF8))
                {
                    w.WriteLine(text);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>把本次运行缓冲区写入指定文件，返回路径（失败返回 null）。</summary>
        internal static string Flush(string fileName)
        {
            Exception first = null;
            try
            {
                EnsureDir();
                string path = Path.Combine(DataDir, fileName);
                using (StreamWriter w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.Write(Buffer.ToString());
                }
                return path;
            }
            catch (Exception ex)
            {
                first = ex;
            }

            // 首选位置失败时退到程序目录，保证证据不会丢
            try
            {
                string path = Path.Combine(AppPaths.ExeDir, fileName);
                using (StreamWriter w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.Write(Buffer.ToString());
                    w.Write("[[primary write failed: " + (first == null ? "?" : first.Message) + "]]");
                }
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void EnsureDir()
        {
            string dir = DataDir;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
