using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ImageView.Core
{
    /// <summary>
    /// 性能基准：用同一套口径量启动、内存与关键操作耗时，方便和方案里的目标值对比。
    /// 用法: ImageView.exe --bench [图片路径]
    /// </summary>
    internal static class Bench
    {
        internal static int Run(string[] files)
        {
            AppLog.Line("===== ImageView 基准 =====");
            AppLog.Line("OS: {0}  .NET: {1}", Environment.OSVersion.VersionString, Environment.Version);
            AppLog.Line("WIC: {0}", WicDecoder.Available);
            Theme.Refresh();

            Stopwatch clock = Program.Clock;

            // --- A. 空窗口 ---
            ViewerForm form = new ViewerForm(new string[0]);
            long created = clock.ElapsedMilliseconds;
            form.Show();
            form.Refresh();
            Application.DoEvents();
            long painted = clock.ElapsedMilliseconds;
            AppLog.Line("");
            AppLog.Line("[A] 空窗口");
            AppLog.Line("    窗口={0} 客户区={1}", form.Size, form.ClientSize);
            AppLog.Line("    exe到窗体创建 = {0} ms", created);
            AppLog.Line("    exe到首帧绘制 = {0} ms", painted);
            Report("    内存");

            string path = files != null && files.Length > 0 ? files[0] : null;
            if (!string.IsNullOrEmpty(path))
            {
                // --- B. 打开图片 ---
                Stopwatch sw = Stopwatch.StartNew();
                form.OpenPath(path, true);
                sw.Stop();
                long openMs = sw.ElapsedMilliseconds;
                sw.Restart();
                form.Refresh();
                sw.Stop();
                long firstPaint = sw.ElapsedMilliseconds;
                AppLog.Line("");
                AppLog.Line("[B] 打开图片 {0}", System.IO.Path.GetFileName(path));
                AppLog.Line("    解码到首屏可用 = {0} ms", openMs);
                AppLog.Line("    适应窗口绘制   = {0} ms", firstPaint);
                Report("    内存");

                // 适应窗口下的绘制分解（含圆角卡片/投影的成本）
                ViewerForm.Profile = true;
                sw.Restart();
                for (int i = 0; i < 8; i++) { form.Refresh(); }
                sw.Stop();
                ViewerForm.Profile = false;
                AppLog.Line("    适应窗口 8 帧 = {0:0.0} ms/帧    {1}", sw.ElapsedMilliseconds / 8.0,
                    ViewerForm.ProfileReport());

                // --- C. 放大到 100%+ 后的重绘 ---
                sw.Restart();
                form.Canvas.ZoomTo(1.0);
                form.Refresh();
                sw.Stop();
                long actualPaint = sw.ElapsedMilliseconds;
                AppLog.Line("");
                AppLog.Line("[C] 缩放");
                AppLog.Line("    1:1 重绘 = {0} ms   比例={1}", actualPaint, Toolbar.FormatScale(form.Canvas.Scale));
                Report("    内存");

                // --- D. 拖拽平移 (8 次) ---
                form.Canvas.ZoomTo(2.0);
                form.Refresh();

                // 先测一次“什么都不变只重绘”的底噪，用来区分画布成本与窗口绘制成本
                ViewerForm.Profile = true;
                sw.Restart();
                for (int i = 0; i < 8; i++)
                {
                    form.Refresh();
                }
                ViewerForm.Profile = false;
                sw.Stop();
                double basePaint = sw.ElapsedMilliseconds / 8.0;

                sw.Restart();
                for (int i = 0; i < 8; i++)
                {
                    form.Canvas.PanBy(i % 2 == 0 ? 40 : -40, 12);
                    form.Refresh();
                }
                sw.Stop();
                AppLog.Line("");
                AppLog.Line("[D] 2 倍缩放");
                AppLog.Line("    仅重绘基准 = {0:0.0} ms/帧", basePaint);
                AppLog.Line("    {0}", ViewerForm.ProfileReport());
                AppLog.Line("    拖拽 8 帧   = {0:0.0} ms/帧", sw.ElapsedMilliseconds / 8.0);
                Report("    内存");

                // --- E. 静止后高清重绘 ---
                form.Canvas.ZoomToFit();
                sw.Restart();
                form.Refresh();
                sw.Stop();
                AppLog.Line("");
                AppLog.Line("[E] 回到适应窗口 = {0} ms", sw.ElapsedMilliseconds);
                Report("    内存");
            }

            form.Close();
            form.Dispose();
            Application.DoEvents();

            long size = 0;
            try
            {
                size = new System.IO.FileInfo(Application.ExecutablePath).Length;
            }
            catch (Exception)
            {
            }
            AppLog.Line("");
            AppLog.Line("exe_kb={0:N1}", size / 1024.0);
            AppLog.Line("BENCH OK");
            return 0;
        }

        private static void Report(string label)
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    p.Refresh();
                    AppLog.Line("{0}: 工作集={1:N1}MB 私有={2:N1}MB 峰值={3:N1}MB",
                        label,
                        p.WorkingSet64 / 1048576.0,
                        p.PrivateMemorySize64 / 1048576.0,
                        p.PeakWorkingSet64 / 1048576.0);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Report", ex);
            }
        }
    }
}
