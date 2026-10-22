using System;
using System.IO;

namespace ImageView
{
    /// <summary>
    /// 数据目录解析。优先 %LOCALAPPDATA%\ImageView；不可写时依次退到
    /// 程序目录下的 data\、以及程序目录本身，保证在受限账户/只读环境下仍能工作。
    /// </summary>
    internal static class AppPaths
    {
        private static string _dataDir;
        private static string _resolvedBy;

        /// <summary>可写的数据目录（配置文件、日志）。可能等于程序目录。</summary>
        internal static string DataDir
        {
            get
            {
                if (_dataDir == null)
                {
                    _dataDir = Resolve();
                }
                return _dataDir;
            }
        }

        internal static string ResolvedBy
        {
            get
            {
                if (_resolvedBy == null)
                {
                    _dataDir = Resolve();
                }
                return _resolvedBy;
            }
        }

        internal static string SettingsPath
        {
            get { return Path.Combine(DataDir, "settings.ini"); }
        }

        internal static string LogPath(string fileName)
        {
            return Path.Combine(DataDir, fileName);
        }

        internal static string ExeDir
        {
            get
            {
                try
                {
                    string dir = AppDomain.CurrentDomain.BaseDirectory;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        return dir.TrimEnd(Path.DirectorySeparatorChar);
                    }
                }
                catch (Exception)
                {
                }
                return Environment.CurrentDirectory;
            }
        }

        private static string Resolve()
        {
            string localAppData = null;
            try
            {
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            catch (Exception)
            {
            }

            string[] candidates = new string[]
            {
                string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "ImageView"),
                Path.Combine(ExeDir, "data"),
                ExeDir
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string dir = candidates[i];
                if (dir == null)
                {
                    continue;
                }
                if (TryPrepare(dir))
                {
                    _resolvedBy = i == 0 ? "localappdata" : (i == 1 ? "exe-data" : "exe-dir");
                    return dir;
                }
            }

            _resolvedBy = "fallback";
            return ExeDir;
        }

        private static bool TryPrepare(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // 真正做一次写入探测，避免“目录存在但不可写”
                string probe = Path.Combine(dir, ".write-probe");
                using (FileStream fs = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                    fs.WriteByte(0);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
