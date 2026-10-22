using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace ImageView.Core
{
    /// <summary>
    /// 文件关联：只写 HKCU（免管理员、可逆）。
    /// Win10 不允许程序静默抢占默认打开方式，所以这里只做“登记到系统 + 引导用户去设置页选择”。
    /// 绝不写 UserChoice（写了也会被系统回滚）。
    /// </summary>
    internal static class Assoc
    {
        internal const string ProgId = "ImageView.Image";
        internal const string RegisteredName = "ImageView";

        /// <summary>最近一次失败的类型名，便于区分“环境不允许”和“逻辑错误”。</summary>
        internal static string LastErrorType = string.Empty;

        internal static string ExePath
        {
            get
            {
                try
                {
                    return System.Reflection.Assembly.GetEntryAssembly().Location;
                }
                catch (Exception)
                {
                    return AppPaths.ExeDir + "\\ImageView.exe";
                }
            }
        }

        private static string ClassesRoot
        {
            get { return @"Software\Classes\"; }
        }

        internal static bool IsRegistered()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ClassesRoot + ProgId))
                {
                    return key != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool Register(out string error)
        {
            error = null;
            try
            {
                string exe = ExePath;
                string command = "\"" + exe + "\" \"%1\"";

                using (RegistryKey progId = Registry.CurrentUser.CreateSubKey(ClassesRoot + ProgId))
                {
                    progId.SetValue(null, "图片文件");
                    progId.SetValue("FriendlyTypeName", "图片文件");
                    progId.SetValue("AlwaysShowExt", string.Empty, RegistryValueKind.String);
                }
                using (RegistryKey icon = Registry.CurrentUser.CreateSubKey(ClassesRoot + ProgId + @"\DefaultIcon"))
                {
                    icon.SetValue(null, "\"" + exe + "\",0");
                }
                using (RegistryKey open = Registry.CurrentUser.CreateSubKey(ClassesRoot + ProgId + @"\shell\open"))
                {
                    open.SetValue(null, "打开");
                }
                using (RegistryKey cmd = Registry.CurrentUser.CreateSubKey(ClassesRoot + ProgId + @"\shell\open\command"))
                {
                    cmd.SetValue(null, command);
                }

                // “打开方式”列表
                using (RegistryKey app = Registry.CurrentUser.CreateSubKey(
                    ClassesRoot + @"Applications\ImageView.exe\shell\open\command"))
                {
                    app.SetValue(null, command);
                }
                using (RegistryKey friendly = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"Applications\ImageView.exe"))
                {
                    friendly.SetValue("FriendlyAppName", "ImageView 图片查看器");
                }
                using (RegistryKey types = Registry.CurrentUser.CreateSubKey(
                    ClassesRoot + @"Applications\ImageView.exe\SupportedTypes"))
                {
                    for (int i = 0; i < Formats.SupportedExtensions.Length; i++)
                    {
                        types.SetValue(Formats.SupportedExtensions[i], string.Empty);
                    }
                }

                // 逐个扩展名登记为“可选打开方式”（不改用户当前默认）
                for (int i = 0; i < Formats.SupportedExtensions.Length; i++)
                {
                    using (RegistryKey ext = Registry.CurrentUser.CreateSubKey(
                        ClassesRoot + Formats.SupportedExtensions[i] + @"\OpenWithProgids"))
                    {
                        ext.SetValue(ProgId, string.Empty, RegistryValueKind.String);
                    }
                }

                // 写入 Capabilities，让本程序出现在“默认应用”里
                using (RegistryKey caps = Registry.CurrentUser.CreateSubKey(@"Software\ImageView\Capabilities"))
                {
                    caps.SetValue("ApplicationName", "ImageView 图片查看器");
                    caps.SetValue("ApplicationDescription", "轻量图片查看器");
                }
                using (RegistryKey files = Registry.CurrentUser.CreateSubKey(
                    @"Software\ImageView\Capabilities\FileAssociations"))
                {
                    for (int i = 0; i < Formats.SupportedExtensions.Length; i++)
                    {
                        files.SetValue(Formats.SupportedExtensions[i], ProgId);
                    }
                }
                using (RegistryKey registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                {
                    registered.SetValue(RegisteredName, @"Software\ImageView\Capabilities");
                }

                Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Assoc.Register", ex);
                LastErrorType = ex.GetType().Name;
                error = ex.Message;
                return false;
            }
        }

        internal static bool Unregister(out string error)
        {
            error = null;
            try
            {
                DeleteTree(@"Software\Classes\" + ProgId);
                DeleteTree(@"Software\Classes\Applications\ImageView.exe");
                DeleteTree(@"Software\ImageView");
                using (RegistryKey registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
                {
                    if (registered != null)
                    {
                        registered.DeleteValue(RegisteredName, false);
                    }
                }
                for (int i = 0; i < Formats.SupportedExtensions.Length; i++)
                {
                    try
                    {
                        using (RegistryKey ext = Registry.CurrentUser.OpenSubKey(
                            @"Software\Classes\" + Formats.SupportedExtensions[i] + @"\OpenWithProgids", true))
                        {
                            if (ext != null)
                            {
                                ext.DeleteValue(ProgId, false);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Assoc.Unregister", ex);
                error = ex.Message;
                return false;
            }
        }

        private static void DeleteTree(string subKey)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>打开 Windows 的“默认应用”设置页。</summary>
        internal static bool OpenDefaultAppsSettings()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("ms-settings:defaultapps");
                info.UseShellExecute = true;
                Process.Start(info);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Assoc.OpenDefaultAppsSettings", ex);
                try
                {
                    ProcessStartInfo info = new ProcessStartInfo("control.exe", " /name Microsoft.DefaultPrograms");
                    info.UseShellExecute = true;
                    Process.Start(info);
                    return true;
                }
                catch (Exception inner)
                {
                    AppLog.Error("Assoc.OpenDefaultAppsSettings fallback", inner);
                    return false;
                }
            }
        }
    }
}
