using System;
using System.Drawing;
using System.IO;
using System.Text;

namespace ImageView.Core
{
    /// <summary>
    /// 设置：极简 key=value 文本（不用 JSON，避免引入依赖）。
    /// 文件损坏或缺失时全部退回默认值，绝不抛异常。
    /// </summary>
    internal sealed class Settings
    {
        private static Settings _current;

        internal static Settings Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Load();
                }
                return _current;
            }
        }

        /// <summary>默认深色（适合看图）；可在 ⋯ 菜单 / Ctrl+T / 设置里改成浅色或跟随系统。</summary>
        internal ThemeMode Theme = ThemeMode.Dark;
        internal bool Filmstrip = true;
        internal bool HasWindowBounds;
        internal Rectangle WindowBounds;
        internal bool Maximized;
        internal string LastSaveDir;

        internal static string FilePath
        {
            get { return AppPaths.SettingsPath; }
        }

        internal static Settings Load()
        {
            Settings settings = new Settings();
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return settings;
                }
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    settings.Apply(key, value);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Settings.Load", ex);
            }
            return settings;
        }

        private void Apply(string key, string value)
        {
            switch (key)
            {
                case "theme":
                {
                    int mode;
                    if (int.TryParse(value, out mode) && mode >= 0 && mode <= 2)
                    {
                        Theme = (ThemeMode)mode;
                    }
                    break;
                }
                case "filmstrip":
                    Filmstrip = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "window":
                {
                    string[] parts = value.Split(',');
                    int x, y, w, h;
                    if (parts.Length == 4 &&
                        int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) &&
                        int.TryParse(parts[2], out w) && int.TryParse(parts[3], out h) && w > 200 && h > 150)
                    {
                        WindowBounds = new Rectangle(x, y, w, h);
                        HasWindowBounds = true;
                    }
                    break;
                }
                case "maximized":
                    Maximized = value == "1";
                    break;
                case "lastsavedir":
                    LastSaveDir = value;
                    break;
            }
        }

        internal void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ImageView 设置");
                sb.AppendLine("theme=" + (int)Theme);
                sb.AppendLine("filmstrip=" + (Filmstrip ? "1" : "0"));
                if (HasWindowBounds)
                {
                    sb.AppendLine("window=" + WindowBounds.X + "," + WindowBounds.Y + "," +
                                  WindowBounds.Width + "," + WindowBounds.Height);
                }
                sb.AppendLine("maximized=" + (Maximized ? "1" : "0"));
                if (!string.IsNullOrEmpty(LastSaveDir))
                {
                    sb.AppendLine("lastsavedir=" + LastSaveDir);
                }

                string path = FilePath;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                AppLog.Error("Settings.Save", ex);
            }
        }
    }
}
