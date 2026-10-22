using System;
using System.Drawing;
using Microsoft.Win32;

namespace ImageView
{
    internal enum ThemeMode
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>一套配色。所有绘制只从这里取色，切换主题时整体替换。</summary>
    internal sealed class Palette
    {
        internal bool Dark;

        internal Color Backdrop;     // 图片背后的画布背景
        internal Color Surface;      // 标题栏 / 工具栏 / 浮层底
        internal Color SurfaceAlt;   // 悬浮态 / 次级面
        internal Color SurfaceSunken; // 输入框等凹陷面
        internal Color Border;
        internal Color BorderStrong;
        internal Color Text;
        internal Color TextDim;
        internal Color Accent;
        internal Color AccentSoft;   // 强调色低透明背景
        internal Color OnAccent;
        internal Color Danger;
        internal Color Shadow;
        internal Color CheckerA;
        internal Color CheckerB;
        internal Color Scrim;        // 浮层背景压暗

        internal static Palette CreateDark()
        {
            Palette p = new Palette();
            p.Dark = true;
            p.Backdrop = Color.FromArgb(0x13, 0x13, 0x16);
            p.Surface = Color.FromArgb(0x1C, 0x1C, 0x20);
            p.SurfaceAlt = Color.FromArgb(0x27, 0x27, 0x2C);
            p.SurfaceSunken = Color.FromArgb(0x17, 0x17, 0x1A);
            p.Border = Color.FromArgb(0x2F, 0x2F, 0x36);
            p.BorderStrong = Color.FromArgb(0x3D, 0x3D, 0x46);
            p.Text = Color.FromArgb(0xED, 0xED, 0xF1);
            p.TextDim = Color.FromArgb(0x8E, 0x8E, 0x98);
            p.Accent = Color.FromArgb(0x6E, 0x8C, 0xF5);
            p.AccentSoft = Color.FromArgb(0x2E, 0x6E, 0x8C, 0xF5);
            p.OnAccent = Color.FromArgb(0xFF, 0xFF, 0xFF);
            p.Danger = Color.FromArgb(0xE5, 0x48, 0x4D);
            p.Shadow = Color.FromArgb(0x8C, 0x00, 0x00, 0x00);
            p.CheckerA = Color.FromArgb(0x18, 0x18, 0x1B);
            p.CheckerB = Color.FromArgb(0x1F, 0x1F, 0x23);
            p.Scrim = Color.FromArgb(0xB4, 0x00, 0x00, 0x00);
            return p;
        }

        internal static Palette CreateLight()
        {
            Palette p = new Palette();
            p.Dark = false;
            // 参考图是一套很“轻”的浅色：近乎白底、极淡描边、蓝紫色强调
            p.Backdrop = Color.FromArgb(0xF6, 0xF6, 0xF8);
            p.Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);
            p.SurfaceAlt = Color.FromArgb(0xF2, 0xF2, 0xF5);
            p.SurfaceSunken = Color.FromArgb(0xF5, 0xF5, 0xF8);
            p.Border = Color.FromArgb(0xE9, 0xE9, 0xEE);
            p.BorderStrong = Color.FromArgb(0xDA, 0xDA, 0xE1);
            p.Text = Color.FromArgb(0x1B, 0x1B, 0x1F);
            p.TextDim = Color.FromArgb(0x8B, 0x8B, 0x94);
            p.Accent = Color.FromArgb(0x5B, 0x7B, 0xE8);
            p.AccentSoft = Color.FromArgb(0x26, 0x5B, 0x7B, 0xE8);
            p.OnAccent = Color.FromArgb(0xFF, 0xFF, 0xFF);
            p.Danger = Color.FromArgb(0xE5, 0x48, 0x4D);
            p.Shadow = Color.FromArgb(0x14, 0x11, 0x11, 0x1A);
            p.CheckerA = Color.FromArgb(0xF4, 0xF4, 0xF7);
            p.CheckerB = Color.FromArgb(0xE9, 0xE9, 0xEF);
            p.Scrim = Color.FromArgb(0x73, 0x00, 0x00, 0x00);
            return p;
        }
    }

    /// <summary>主题：跟随系统 / 强制明暗。系统主题变化时通知订阅者刷新。</summary>
    internal static class Theme
    {
        private static ThemeMode _mode = ThemeMode.Dark;
        private static Palette _palette = Palette.CreateDark();
        private static bool _isDark = true;
        private static bool _initialized;

        internal static Palette P { get { return _palette; } }
        internal static bool IsDark { get { return _isDark; } }

        internal static ThemeMode Mode
        {
            get { return _mode; }
            set
            {
                if (_mode == value)
                {
                    return;
                }
                _mode = value;
                // 统一在这里持久化：菜单里的「外观」和设置面板里的选项都走这条路径，
                // 避免“从这里切了、下次启动又变回去”。
                if (PersistChanges)
                {
                    try
                    {
                        Core.Settings.Current.Theme = value;
                        Core.Settings.Current.Save();
                    }
                    catch (Exception)
                    {
                    }
                }
                Refresh(true);
            }
        }

        /// <summary>自检/批量切换时可关掉自动保存，避免频繁写盘。</summary>
        internal static bool PersistChanges = true;

        /// <summary>模式的中文名（菜单和提示共用）。</summary>
        internal static string Label(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Light:
                    return Strings.MenuThemeLight;
                case ThemeMode.Dark:
                    return Strings.MenuThemeDark;
                default:
                    return Strings.MenuThemeSystem;
            }
        }

        /// <summary>按 跟随系统 → 浅色 → 深色 → 跟随系统 循环。</summary>
        internal static ThemeMode NextMode()
        {
            switch (_mode)
            {
                case ThemeMode.System:
                    return ThemeMode.Light;
                case ThemeMode.Light:
                    return ThemeMode.Dark;
                default:
                    return ThemeMode.System;
            }
        }

        internal static event EventHandler Changed;

        /// <summary>按当前模式重新解析配色；changed 为 true 表示确实变化了。</summary>
        internal static void Refresh()
        {
            Refresh(false);
        }

        internal static void Refresh(bool force)
        {
            bool dark = _mode == ThemeMode.System ? DetectSystemDark() : _mode == ThemeMode.Dark;
            bool changed = !_initialized || dark != _isDark || force;
            _isDark = dark;
            _palette = dark ? Palette.CreateDark() : Palette.CreateLight();
            PaintKit.Invalidate();
            _initialized = true;
            if (changed)
            {
                EventHandler h = Changed;
                if (h != null)
                {
                    h(null, EventArgs.Empty);
                }
            }
        }

        internal static bool DetectSystemDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value is int)
                        {
                            return (int)value == 0;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 读不到就按暗色处理（看图场景更常用）
            }
            return true;
        }
    }

    /// <summary>DPI 缩放。所有界面尺寸都过这里，保证 100%/125%/150% 一致。</summary>
    internal static class Ui
    {
        internal static float Scale = 1f;

        internal static void SetDpi(uint dpi)
        {
            float s = dpi / 96f;
            if (s < 0.5f)
            {
                s = 1f;
            }
            if (s > 4f)
            {
                s = 4f;
            }
            Scale = s;
        }

        internal static int S(int value)
        {
            return (int)Math.Round(value * (double)Scale, MidpointRounding.AwayFromZero);
        }

        internal static float SF(float value)
        {
            return value * Scale;
        }

        internal static Rectangle S(Rectangle r)
        {
            return new Rectangle(S(r.X), S(r.Y), S(r.Width), S(r.Height));
        }

        internal static RectangleF S(RectangleF r)
        {
            return new RectangleF(SF(r.X), SF(r.Y), SF(r.Width), SF(r.Height));
        }
    }

    /// <summary>
    /// 字体。用“点”为单位创建，GDI+ 会按显示器 DPI 自动换算，
    /// 所以这里不再乘 Ui.Scale（否则会双重放大）。
    /// </summary>
    internal static class Fonts
    {
        private const string Family = "Segoe UI";
        private const string FamilyStrong = "Segoe UI Semibold";

        internal static readonly Font Title = Create(FamilyStrong, 10.5f);
        internal static readonly Font Body = Create(Family, 9f);
        internal static readonly Font BodyStrong = Create(FamilyStrong, 9f);
        internal static readonly Font Small = Create(Family, 8.25f);
        internal static readonly Font Tiny = Create(Family, 7.5f);
        internal static readonly Font Large = Create(FamilyStrong, 12f);
        internal static readonly Font Huge = Create(Family, 15f);

        private static Font Create(string family, float points)
        {
            try
            {
                return new Font(family, points, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (Exception)
            {
                try
                {
                    return new Font(FontFamily.GenericSansSerif, points, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch (Exception)
                {
                    return new Font(FontFamily.GenericSansSerif, points);
                }
            }
        }
    }
}
