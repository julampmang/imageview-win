using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ImageView
{
    internal static class Program
    {
        internal static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        internal static string[] StartupFiles = new string[0];

        /// <summary>截图/演示用：启动后直接打开某个浮层（menu / resize / settings / info）。</summary>
        internal static string DemoMode;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            List<string> files = new List<string>();
            string mode = "run";
            ThemeMode? forcedTheme = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? string.Empty;
                if (Is(arg, "--selftest"))
                {
                    mode = "selftest";
                }
                else if (Is(arg, "--bench"))
                {
                    mode = "bench";
                }
                else if (Is(arg, "--help") || arg == "/?" || arg == "-h")
                {
                    mode = "help";
                }
                else if (Is(arg, "--version"))
                {
                    mode = "version";
                }
                else if (Is(arg, "--probe"))
                {
                    mode = "probe";
                }
                else if (arg.StartsWith("--demo=", StringComparison.OrdinalIgnoreCase))
                {
                    DemoMode = arg.Substring("--demo=".Length);
                }
                else if (arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = arg.Substring("--theme=".Length);
                    if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase))
                    {
                        forcedTheme = ThemeMode.Light;
                    }
                    else if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase))
                    {
                        forcedTheme = ThemeMode.Dark;
                    }
                    else
                    {
                        forcedTheme = ThemeMode.System;
                    }
                }
                else if (arg.Length > 1 && arg[0] == '-' && arg[1] == '-')
                {
                    // 未知开关，忽略
                }
                else if (arg.Length > 0)
                {
                    files.Add(arg);
                }
            }

            StartupFiles = files.ToArray();

            if (mode == "help")
            {
                AppLog.EnableConsole();
                AppLog.Line(Strings.HelpText);
                AppLog.Flush("help.log");
                return 0;
            }
            if (mode == "version")
            {
                AppLog.EnableConsole();
                AppLog.Line(Strings.AppId + " " + Strings.Version);
                AppLog.Flush("version.log");
                return 0;
            }
            if (mode == "selftest")
            {
                AppLog.EnableConsole();
                int code = Core.SelfTest.Run();
                AppLog.Flush("selftest.log");
                return code;
            }
            if (mode == "bench")
            {
                AppLog.EnableConsole();
                int code = Core.Bench.Run(StartupFiles);
                AppLog.Flush("bench.log");
                return code;
            }
            if (mode == "probe")
            {
                AppLog.EnableConsole();
                int code = Core.Probe.Run(StartupFiles);
                AppLog.Flush("probe.log");
                return code;
            }

            if (forcedTheme.HasValue)
            {
                Theme.Mode = forcedTheme.Value;
            }
            else
            {
                Theme.Mode = Core.Settings.Current.Theme;
            }

            try
            {
                Theme.Refresh();
            }
            catch (Exception ex)
            {
                AppLog.Error("Theme.Refresh", ex);
            }

            try
            {
                ViewerForm form = new ViewerForm(StartupFiles);
                Application.Run(form);
                return 0;
            }
            catch (Exception ex)
            {
                AppLog.Error("Application.Run", ex);
                try
                {
                    MessageBox.Show(ex.Message, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception)
                {
                }
                return 1;
            }
        }

        private static bool Is(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            AppLog.Error("UI Thread", e.Exception);
            try
            {
                MessageBox.Show(
                    "发生了一个意外错误，程序会继续运行。\r\n\r\n" + e.Exception.Message,
                    Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception)
            {
            }
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            AppLog.Error("Domain", e.ExceptionObject as Exception);
        }
    }
}
