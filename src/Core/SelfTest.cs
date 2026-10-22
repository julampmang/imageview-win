using System;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;

namespace ImageView.Core
{
    /// <summary>
    /// 自检：不依赖人工观察，用断言覆盖关键逻辑。
    /// 退出码 0 = 全部通过。
    /// </summary>
    internal static class SelfTest
    {
        private static int _passed;
        private static int _failed;

        internal static int Run()
        {
            _passed = 0;
            _failed = 0;
            AppLog.Line("===== ImageView 自检 =====");
            AppLog.Line("时间: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            AppLog.Line("数据目录: {0}（{1}）", AppPaths.DataDir, AppPaths.ResolvedBy);

            CharsetTests();
            ThemeTests();
            GeometryTests();
            FormatTests();
            DecodeTests();
            AnimationTests();
            CanvasTests();
            HistogramTests();
            EditTests();
            EditGeometryTests();
            BatchTests();
            ResizeTests();
            SettingsTests();
            AssocTests();
            CleanupTemp();

            AppLog.Line("------------------------------");
            AppLog.Line("结果: 通过 {0} 项，失败 {1} 项", _passed, _failed);
            AppLog.Line(_failed == 0 ? "SELFTEST OK" : "SELFTEST FAILED");
            return _failed == 0 ? 0 : 1;
        }

        // ---------- 断言 ----------

        private static void Check(string name, bool condition, string detail)
        {
            if (condition)
            {
                _passed++;
                AppLog.Line("  [PASS] {0}", name);
            }
            else
            {
                _failed++;
                AppLog.Line("  [FAIL] {0}  {1}", name, detail ?? string.Empty);
            }
        }

        private static void CheckEquals(string name, string expected, string actual)
        {
            Check(name, string.Equals(expected, actual, StringComparison.Ordinal),
                string.Format("期望 [{0}] 实际 [{1}]", expected, actual));
        }

        private static void CheckEquals(string name, long expected, long actual)
        {
            Check(name, expected == actual, string.Format("期望 {0} 实际 {1}", expected, actual));
        }

        // ---------- 路径 ----------

        private static string TestsRoot()
        {
            string[] candidates = new string[]
            {
                Path.GetFullPath(Path.Combine(AppPaths.ExeDir, "..", "tests")),
                Path.Combine(AppPaths.ExeDir, "tests"),
                Path.Combine(Environment.CurrentDirectory, "tests")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }
            return null;
        }

        private static string AssetPath(string name)
        {
            string root = TestsRoot();
            if (root == null)
            {
                return null;
            }
            string path = Path.Combine(root, "assets", name);
            return File.Exists(path) ? path : null;
        }

        private static string TempDir()
        {
            string root = TestsRoot() ?? AppPaths.DataDir;
            string dir = Path.Combine(root, "tmp");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        // ---------- 具体检查 ----------

        /// <summary>
        /// 源码用 UTF-8 保存并按 /codepage:65001 编译。如果编码设置出错，
        /// 中文字面量会变成乱码，这里用同值的 Unicode 转义来校验。
        /// </summary>
        private static void CharsetTests()
        {
            CheckEquals("中文文案: 应用名", "\u56FE\u7247\u67E5\u770B\u5668", Strings.AppName);
            CheckEquals("中文文案: 调整尺寸", "\u8C03\u6574\u5C3A\u5BF8\u2026", Strings.TipResize.Replace("  (Ctrl+R)", string.Empty));
            CheckEquals("中文文案: 适应窗口", "\u9002\u5E94\u7A97\u53E3", Strings.TipFit.Replace("  (0)", string.Empty));
            CheckEquals("中文文案: 删除确认", "\u5220\u9664\u8FD9\u5F20\u56FE\u7247\uFF1F", Strings.DeleteTitle);
        }

        private static void ThemeTests()
        {
            ThemeMode modeSaved = Theme.Mode;
            Theme.PersistChanges = false;

            Theme.Mode = ThemeMode.Dark;
            Check("暗色主题生效", Theme.IsDark, "Theme.Mode=Dark 但 IsDark=false");
            Check("暗色: 背景比文字暗", Theme.P.Backdrop.GetBrightness() < Theme.P.Text.GetBrightness(),
                string.Format("backdrop={0} text={1}", Theme.P.Backdrop, Theme.P.Text));

            Theme.Mode = ThemeMode.Light;
            Check("浅色主题生效", !Theme.IsDark, "Theme.Mode=Light 但 IsDark=true");
            Check("浅色: 背景比文字亮", Theme.P.Backdrop.GetBrightness() > Theme.P.Text.GetBrightness(),
                string.Format("backdrop={0} text={1}", Theme.P.Backdrop, Theme.P.Text));

            Theme.Mode = modeSaved;
            Theme.PersistChanges = true;

            Ui.SetDpi(96);
            Check("DPI 100% 缩放", Math.Abs(Ui.Scale - 1f) < 0.001f, "Ui.Scale=" + Ui.Scale);
            Ui.SetDpi(120);
            Check("DPI 125% 缩放", Math.Abs(Ui.Scale - 1.25f) < 0.001f, "Ui.Scale=" + Ui.Scale);
            CheckEquals("DPI 缩放取整", 55, Ui.S(44));
            Ui.SetDpi(144);
            CheckEquals("DPI 150% 缩放取整", 66, Ui.S(44));
            Ui.SetDpi(96);
        }

        private static void GeometryTests()
        {
            using (Bitmap bmp = new Bitmap(8, 8))
            using (Graphics g = Graphics.FromImage(bmp))
            using (System.Drawing.Drawing2D.GraphicsPath path = PaintKit.RoundRect(
                new RectangleF(0f, 0f, 100f, 50f), 10f))
            {
                RectangleF bounds = path.GetBounds();
                Check("圆角矩形外包尺寸", Math.Abs(bounds.Width - 100f) < 1.5f && Math.Abs(bounds.Height - 50f) < 1.5f,
                    string.Format("bounds={0}", bounds));

                SizeF text = PaintKit.Measure(g, "测试文本", Fonts.Body);
                Check("文本测量非零", text.Width > 0f && text.Height > 0f, string.Format("size={0}", text));

                try
                {
                    foreach (IconId id in Enum.GetValues(typeof(IconId)))
                    {
                        PaintKit.Icon(g, id, new RectangleF(0f, 0f, 32f, 32f), Color.White, 1.5f);
                    }
                    PaintKit.Shadow(g, new RectangleF(10f, 10f, 40f, 40f), 8f, 6, 60);
                    PaintKit.Checker(g, new RectangleF(0f, 0f, 40f, 40f), 8, Color.White, Color.Black);
                    Check("图标/阴影/棋盘格可绘制", true, null);
                }
                catch (Exception ex)
                {
                    Check("图标/阴影/棋盘格可绘制", false, ex.Message);
                }
            }

            SolidBrush a = PaintKit.Brush(Color.Red);
            SolidBrush b = PaintKit.Brush(Color.Red);
            Check("画刷缓存复用", ReferenceEquals(a, b), "相同颜色返回了不同实例");
        }

        private static void FormatTests()
        {
            CheckEquals("格式名 PNG", "PNG", Formats.FormatName("a.b.PNG"));
            CheckEquals("格式名 WebP", "WebP", Formats.FormatName("x.webp"));
            Check("扩展名识别", Formats.IsImageFile("a\\b\\c.jpeg") && !Formats.IsImageFile("a.txt"),
                "IsImageFile 判定错误");

            var formats = GdiEncoder.Formats();
            Check("输出格式探测非空", formats != null && formats.Count >= 2,
                "formats=" + (formats == null ? "null" : formats.Count.ToString()));
            Check("输出格式含 JPEG", GdiEncoder.FindByName("JPEG") != null, "缺少 JPEG 编码器");
            Check("输出格式含 PNG", GdiEncoder.FindByName("PNG") != null, "缺少 PNG 编码器");
            Check("按扩展名找编码器", GdiEncoder.FindByExtension(".jpg") != null, "FindByExtension(.jpg) 失败");
        }

        private static void DecodeTests()
        {
            string corners = AssetPath("corners.png");
            if (corners == null)
            {
                Check("测试图片存在", false, "找不到 tests\\assets，请先运行 tools\\make-testassets.py");
                return;
            }

            using (ImageDocument doc = ImageDocument.Open(corners))
            {
                Check("解码 PNG 有效", doc.IsValid, doc.Error);
                CheckEquals("PNG 宽度", 400, doc.PixelWidth);
                CheckEquals("PNG 高度", 300, doc.PixelHeight);
                Check("PNG 非动图", !doc.IsAnimated, "frameCount=" + doc.FrameCount);
                CheckPixel(doc.Preview, 0.15, 0.15, Color.FromArgb(220, 60, 60), "左上应为红色");
                CheckPixel(doc.Preview, 0.85, 0.15, Color.FromArgb(60, 180, 90), "右上应为绿色");
                CheckPixel(doc.Preview, 0.15, 0.85, Color.FromArgb(60, 100, 220), "左下应为蓝色");
                CheckPixel(doc.Preview, 0.85, 0.85, Color.FromArgb(230, 190, 60), "右下应为黄色");
            }

            string rot6 = AssetPath("rot6.jpg");
            if (rot6 != null)
            {
                using (ImageDocument doc = ImageDocument.Open(rot6))
                {
                    CheckEquals("EXIF 方向识别", 6, doc.Orientation);
                    CheckEquals("方向校正后宽度", 400, doc.PixelWidth);
                    CheckEquals("方向校正后高度", 600, doc.PixelHeight);
                    // 原图 600×400，顺时针转 90° 后：左上=原左下(蓝)，右上=原左上(红)
                    CheckPixel(doc.Preview, 0.15, 0.15, Color.FromArgb(60, 100, 220), "旋转后左上应为蓝色");
                    CheckPixel(doc.Preview, 0.85, 0.15, Color.FromArgb(220, 60, 60), "旋转后右上应为红色");
                }
            }

            string gif = AssetPath("anim.gif");
            if (gif != null)
            {
                using (ImageDocument doc = ImageDocument.Open(gif))
                {
                    Check("GIF 识别为动图", doc.IsAnimated, "IsAnimated=false");
                    Check("GIF 帧数不少于 6", doc.FrameCount >= 6, "实际 " + doc.FrameCount);
                }
            }

            string alpha = AssetPath("alpha.png");
            if (alpha != null)
            {
                using (ImageDocument doc = ImageDocument.Open(alpha))
                {
                    Check("PNG 透明通道识别", doc.HasAlpha, "HasAlpha=false");
                }
            }

            string webp = AssetPath("photo.webp");
            if (webp != null)
            {
                using (ImageDocument doc = ImageDocument.Open(webp))
                {
                    bool ok = doc.IsValid;
                    bool codecPresent = WicDecoder.Available && WicDecoder.CanCreateDecoder(webp);
                    // Win10 的 WebP 是可选系统扩展（GitHub runner 上就没有）；
                    // 连解码器都创建不了 = 这台机器不支持该格式，跳过而不是判失败。
                    if (!codecPresent)
                    {
                        AppLog.Line("  [SKIP] 本机没有 WebP 编解码器（系统可选扩展），跳过 WebP 断言");
                    }
                    else
                    {
                        Check("WebP 解码", ok, doc.Error);
                        CheckEquals("WebP 宽度", 800, doc.PixelWidth);
                    }
                }
            }

            string broken = AssetPath("broken.jpg");
            if (broken != null)
            {
                using (ImageDocument doc = ImageDocument.Open(broken))
                {
                    Check("损坏文件走错误分支", !doc.IsValid && !string.IsNullOrEmpty(doc.Error), "没有报错");
                }
            }

            string tiny = AssetPath("tiny.png");
            if (tiny != null)
            {
                using (ImageDocument doc = ImageDocument.Open(tiny))
                {
                    Check("1×1 极限图可解", doc.IsValid, doc.Error);
                    CheckEquals("1×1 宽度", 1, doc.PixelWidth);
                }
            }

            string big = AssetPath("big.jpg");
            if (big != null)
            {
                using (ImageDocument doc = ImageDocument.Open(big))
                {
                    CheckEquals("大图原始宽度", 8000, doc.SourceWidth);
                    Check("预览档不超过 1600", Math.Max(doc.Preview.Width, doc.Preview.Height) <= 1600,
                        "预览=" + doc.Preview.Width + "x" + doc.Preview.Height);
                    Bitmap tier = doc.GetTier(4000);
                    Check("细节档按需生成", tier != null && Math.Max(tier.Width, tier.Height) <= 4000,
                        tier == null ? "null" : tier.Width + "x" + tier.Height);
                    Check("细节档大于预览", tier != null && tier.Width > doc.Preview.Width,
                        tier == null ? "null" : tier.Width.ToString());

                    int before = doc.MaxCachedSide;
                    Bitmap small = doc.GetTier(600);
                    Check("缩回小档时释放大档", small != null && doc.MaxCachedSide < before,
                        "before=" + before + " after=" + doc.MaxCachedSide);
                }
            }
        }

        private static void CheckPixel(Bitmap bitmap, double fx, double fy, Color expected, string name)
        {
            if (bitmap == null)
            {
                Check(name, false, "位图为空");
                return;
            }
            int x = Math.Min(bitmap.Width - 1, (int)(bitmap.Width * fx));
            int y = Math.Min(bitmap.Height - 1, (int)(bitmap.Height * fy));
            Color actual = bitmap.GetPixel(x, y);
            int delta = Math.Abs(actual.R - expected.R) + Math.Abs(actual.G - expected.G) +
                        Math.Abs(actual.B - expected.B);
            Check(name, delta <= 48, string.Format("期望 {0} 实际 {1}（差 {2}）", expected, actual, delta));
        }

        private static void AnimationTests()
        {
            string gif = AssetPath("anim.gif");
            if (gif == null)
            {
                return;
            }
            GifAnimation animation = new GifAnimation(gif);
            try
            {
                bool loaded = animation.Load();
                Check("动图可加载", loaded, "GifAnimation.Load 返回 false");
                if (!loaded)
                {
                    return;
                }
                Check("动图帧数不少于 6", animation.FrameCount >= 6, "实际 " + animation.FrameCount);

                Color first = animation.Current.GetPixel(8, 8);
                int frames = 0;
                animation.FrameChanged += delegate { frames++; };
                animation.Start();
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < 600)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(8);
                }
                animation.Stop();
                Check("动图自动播放", frames >= 2, "600ms 内只触发 " + frames + " 帧");
                Color later = animation.Current.GetPixel(8, 8);
                Check("动图帧内容确实变化", first != later, first + " -> " + later + "（像素没变说明帧没合成）");
            }
            finally
            {
                animation.Dispose();
            }
        }

        private static void CanvasTests()
        {
            ImageCanvas canvas = new ImageCanvas();
            try
            {
                canvas.SetViewport(new Rectangle(0, 0, 1000, 700));
                // 没有文档时不应崩溃
                canvas.ZoomToFit();
                Check("空画布安全", canvas.Scale > 0, "scale=" + canvas.Scale);

                // 构造一个宽 4000 高 3000 的文档用于缩放数学
                string big = AssetPath("big.jpg");
                if (big == null)
                {
                    return;
                }
                using (ImageDocument doc = ImageDocument.Open(big))
                {
                    canvas.SetDocument(doc);
                    canvas.SetViewport(new Rectangle(0, 0, 1000, 700));
                    canvas.ZoomToFit();
                    double fit = canvas.Scale;
                    Check("适应窗口比例合理", fit > 0.05 && fit <= 1.0, "scale=" + fit);
                    RectangleF rect = canvas.GetDisplayRect();
                    Check("适应窗口时图片居中",
                        Math.Abs(rect.X + rect.Width / 2 - 500) < 1.5 && Math.Abs(rect.Y + rect.Height / 2 - 350) < 1.5,
                        string.Format("rect={0}", rect));

                    canvas.ZoomToActual();
                    Check("1:1 缩放", Math.Abs(canvas.Scale - 1.0) < 0.001, "scale=" + canvas.Scale);

                    canvas.ZoomTo(1000);
                    Check("缩放上限钳制", canvas.Scale <= ImageCanvas.MaxScale + 0.001, "scale=" + canvas.Scale);
                    canvas.ZoomTo(0.0001);
                    Check("缩放下限钳制", canvas.Scale >= ImageCanvas.MinScale - 0.001, "scale=" + canvas.Scale);

                    int widthBefore = doc.PixelWidth;
                    int heightBefore = doc.PixelHeight;
                    canvas.ZoomToFit();
                    canvas.RotateClockwise();
                    RectangleF rotated = canvas.GetDisplayRect();
                    Check("旋转 90° 后显示尺寸互换",
                        Math.Abs(rotated.Width - rotated.Height + (widthBefore - heightBefore) * canvas.Scale) < 8f,
                        string.Format("rect={0} scale={1}", rotated, canvas.Scale));

                    canvas.PanBy(100000f, 100000f);
                    RectangleF panned = canvas.GetDisplayRect();
                    Check("平移被限制在可视范围内", panned.X <= 1000 && panned.Y <= 700,
                        string.Format("rect={0}", panned));
                }
            }
            finally
            {
                canvas.Release();
            }
        }

        private static void ResizeTests()
        {
            CheckEquals("比例换算 4000→2000", 3000L, ResizeJob.ScaleOtherSide(2000, 4000, 6000));
            CheckEquals("比例换算最小值", 1L, ResizeJob.ScaleOtherSide(1, 8000, 6000));
            CheckEquals("百分比换算 50%", 2000L, ResizeJob.SideForPercent(4000, 50));
            Check("尺寸范围校验", ResizeJob.IsValidSide(1) && ResizeJob.IsValidSide(20000) &&
                                !ResizeJob.IsValidSide(0) && !ResizeJob.IsValidSide(20001), "范围校验错误");

            string corners = AssetPath("corners.png");
            if (corners == null)
            {
                return;
            }
            string dir = TempDir();
            string target = Path.Combine(dir, "resized-200x150.png");
            SafeDelete(target);

            using (ImageDocument doc = ImageDocument.Open(corners))
            {
                GdiEncoder.Format format = GdiEncoder.FindByName("PNG");
                ResizeJob.Result result = ResizeJob.Execute(doc, target, 200, 150, format, 90);
                Check("另存为 PNG 成功", result.Ok, result.Error);
                Check("输出文件存在", File.Exists(target), target);
                if (File.Exists(target))
                {
                    using (ImageDocument check = ImageDocument.Open(target))
                    {
                        Check("输出尺寸正确", check.PixelWidth == 200 && check.PixelHeight == 150,
                            check.PixelWidth + "x" + check.PixelHeight);
                        CheckPixel(check.Preview, 0.15, 0.15, Color.FromArgb(220, 60, 60), "缩放后左上仍为红色");
                    }
                }
            }

            // 覆盖写盘：先复制一份，再把自己的尺寸改掉
            string copy = Path.Combine(dir, "overwrite-test.png");
            SafeDelete(copy);
            File.Copy(corners, copy, true);
            using (ImageDocument doc = ImageDocument.Open(copy))
            {
                GdiEncoder.Format format = GdiEncoder.FindByName("PNG");
                ResizeJob.Result result = ResizeJob.Execute(doc, copy, 120, 90, format, 90);
                Check("覆盖原文件成功", result.Ok, result.Error);
            }
            using (ImageDocument check = ImageDocument.Open(copy))
            {
                Check("覆盖后尺寸已变", check.PixelWidth == 120 && check.PixelHeight == 90,
                    check.PixelWidth + "x" + check.PixelHeight);
            }
            string[] leftovers = Directory.GetFiles(dir, "*.tmp");
            Check("没有遗留临时文件", leftovers.Length == 0, "遗留 " + leftovers.Length + " 个");

            // 失败路径：目标被一个同名文件挡住时，必须报错且不动原文件
            string blocker = Path.Combine(dir, "blocker");
            SafeDelete(blocker);
            File.WriteAllText(blocker, "not a directory");
            using (ImageDocument doc = ImageDocument.Open(copy))
            {
                GdiEncoder.Format format = GdiEncoder.FindByName("PNG");
                ResizeJob.Result result = ResizeJob.Execute(doc, Path.Combine(blocker, "x.png"), 60, 45, format, 90);
                Check("目标不可写时返回失败", !result.Ok, "居然成功了");
            }
            using (ImageDocument check = ImageDocument.Open(copy))
            {
                Check("失败后原文件仍然完好", check.IsValid && check.PixelWidth == 120,
                    "原文件被破坏：" + check.Error);
            }
            SafeDelete(blocker);

            // 超范围尺寸被拒绝
            using (ImageDocument doc = ImageDocument.Open(copy))
            {
                ResizeJob.Result result = ResizeJob.Execute(doc, Path.Combine(dir, "bad.png"), 0, 100,
                    GdiEncoder.FindByName("PNG"), 90);
                Check("非法尺寸被拒绝", !result.Ok, "居然成功了");
            }
        }

        private static void EditTests()
        {
            EditRecipe clean = new EditRecipe();
            Check("编辑：默认为恒等", clean.IsIdentity, "IsIdentity=false");
            ColorMatrix identity = clean.BuildMatrix();
            Check("编辑：恒等矩阵对角线为 1", Math.Abs(identity.Matrix00 - 1f) < 1e-6f &&
                Math.Abs(identity.Matrix11 - 1f) < 1e-6f && Math.Abs(identity.Matrix22 - 1f) < 1e-6f,
                identity.Matrix00.ToString("0.000"));
            Check("编辑：恒等矩阵无偏移", Math.Abs(identity.Matrix40) < 1e-6f &&
                Math.Abs(identity.Matrix41) < 1e-6f, identity.Matrix40.ToString("0.0000"));

            // 亮度 +50：中灰应该被抬亮约 64 级（0.5 * 0.5 * 255）
            EditRecipe bright = new EditRecipe();
            bright.Brightness = 50f;
            using (Bitmap source = SolidBitmap(64, 64, Color.FromArgb(255, 128, 128, 128)))
            using (Bitmap output = bright.Apply(source, new Rectangle(0, 0, 64, 64)))
            {
                int level = output.GetPixel(32, 32).R;
                Check("编辑：亮度 +50 抬亮中灰", Math.Abs(level - 192) <= 3, "实际 R=" + level);
            }

            // 饱和度 -100：纯红应该变成灰度
            EditRecipe gray = new EditRecipe();
            gray.Saturation = -100f;
            using (Bitmap source = SolidBitmap(32, 32, Color.FromArgb(255, 200, 40, 40)))
            using (Bitmap output = gray.Apply(source, new Rectangle(0, 0, 32, 32)))
            {
                Color pixel = output.GetPixel(16, 16);
                Check("编辑：饱和度 -100 变灰", Math.Abs(pixel.R - pixel.G) <= 2 && Math.Abs(pixel.G - pixel.B) <= 2,
                    pixel.R + "/" + pixel.G + "/" + pixel.B);
            }

            // 对比度 +50：中点不变，暗部更暗
            EditRecipe contrast = new EditRecipe();
            contrast.Contrast = 50f;
            using (Bitmap source = SolidBitmap(32, 32, Color.FromArgb(255, 64, 64, 64)))
            using (Bitmap output = contrast.Apply(source, new Rectangle(0, 0, 32, 32)))
            {
                int level = output.GetPixel(16, 16).R;
                Check("编辑：对比度 +50 压暗暗部", level < 64, "实际 R=" + level);
            }

            // 自动色阶：低对比图应该被拉满动态范围
            EditRecipe auto = new EditRecipe();
            using (Bitmap low = SolidBitmap(40, 40, Color.FromArgb(255, 100, 100, 100)))
            using (Graphics g = Graphics.FromImage(low))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 190, 190, 190)))
                {
                    g.FillRectangle(brush, 0, 0, 20, 40);
                }
                Histogram histogram = Histogram.Compute(low);
                Check("编辑：自动色阶可算出参数", auto.TryAutoLevels(histogram), "TryAutoLevels=false");
                Check("编辑：自动色阶标记生效", auto.AutoApplied, "AutoApplied=false");
                using (Bitmap output = auto.Apply(low, new Rectangle(0, 0, 40, 40)))
                {
                    int dark = output.GetPixel(30, 20).R;
                    int light = output.GetPixel(5, 20).R;
                    Check("编辑：自动色阶把暗部拉到 0", dark <= 12, "暗部=" + dark);
                    Check("编辑：自动色阶把亮部拉到 255", light >= 243, "亮部=" + light);
                }
            }

            // 裁剪矩形换算
            EditRecipe crop = new EditRecipe();
            crop.Crop = new RectangleF(0.1f, 0.2f, 0.5f, 0.4f);
            Rectangle pixels = crop.CropPixels(1000, 500);
            Check("编辑：裁剪矩形换算", pixels == new Rectangle(100, 100, 500, 200), pixels.ToString());
            Check("编辑：裁剪后不是恒等", !crop.IsIdentity && crop.HasCrop && !crop.HasColor, "状态不对");

            // 预览帧成本：GDI+ 应用 ColorMatrix 画一帧
            EditRecipe previewRecipe = new EditRecipe();
            previewRecipe.Brightness = 20f;
            previewRecipe.Contrast = 15f;
            ColorMatrix matrix = previewRecipe.BuildMatrix();
            using (Bitmap big = SolidBitmap(1600, 1200, Color.FromArgb(255, 90, 110, 130)))
            using (Bitmap screen = new Bitmap(1180, 594, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(screen))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                // 绝对毫秒数在 CI 机器上必然误报（同一台机器 22ms，runner 上 46ms）。
                // 改成同一次运行内的相对比较：颜色变换相对"纯缩放"的开销倍数，
                // 只要这个倍数正常，就说明没有退化成逐像素循环。
                const int rounds = 12;
                g.DrawImage(big, new Rectangle(0, 0, 1180, 594), 0, 0, 1180, 594, GraphicsUnit.Pixel, attributes);

                Stopwatch plainWatch = Stopwatch.StartNew();
                for (int i = 0; i < rounds; i++)
                {
                    g.DrawImage(big, new Rectangle(0, 0, 1180, 594), 0, 0, 1180, 594, GraphicsUnit.Pixel);
                }
                plainWatch.Stop();

                Stopwatch matrixWatch = Stopwatch.StartNew();
                for (int i = 0; i < rounds; i++)
                {
                    g.DrawImage(big, new Rectangle(0, 0, 1180, 594), 0, 0, 1180, 594, GraphicsUnit.Pixel, attributes);
                }
                matrixWatch.Stop();

                // 注意：纯缩放那一轮跑完只要不到 1ms（GDI+ 对相同参数的重复绘制有优化），
                // 拿它当分母算倍数会得出 30 倍这种失真结果。所以用"绝对上限 + 放宽 3 倍余量"：
                // 本机 27ms、GitHub runner 46ms，阈值定 150ms，只用来拦住"退化成逐像素循环"。
                double withMatrix = matrixWatch.Elapsed.TotalMilliseconds / rounds;
                Check("编辑：带颜色变换的单帧绘制 < 150ms", withMatrix < 150.0,
                    withMatrix.ToString("0.0") + " ms");
                AppLog.Line("    带颜色变换单帧耗时: {0:0.0} ms", withMatrix);
            }
            // 端到端：裁剪 + 亮度 → 导出 → 重新打开校验
            string photo = AssetPath("phone-exif.jpg");
            if (photo != null)
            {
                string target = System.IO.Path.Combine(AppPaths.DataDir, "selftest-edit.png");
                try
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                catch (Exception)
                {
                }

                using (ImageDocument document = ImageDocument.Open(photo))
                {
                    Histogram before = Histogram.Compute(document.Preview);
                    EditRecipe export = new EditRecipe();
                    export.Crop = new RectangleF(0f, 0f, 0.5f, 0.5f);
                    export.Brightness = 40f;
                    GdiEncoder.Format format = GdiEncoder.FindByExtension(".png");
                    EditExporter.Result result = EditExporter.Execute(document, export, target, format, 92);
                    Check("编辑：导出成功", result.Ok, result.Error);
                    Check("编辑：导出尺寸为裁剪后一半", result.Width == 600 && result.Height == 450,
                        result.Width + "x" + result.Height);
                    Check("编辑：导出文件存在", File.Exists(target), target);

                    using (ImageDocument saved = ImageDocument.Open(target))
                    {
                        Check("编辑：重新打开成功", saved.IsValid, saved.Error);
                        Histogram after = Histogram.Compute(saved.Preview);
                        Check("编辑：导出后更亮", after.MeanLuma > before.MeanLuma + 25,
                            before.MeanLuma.ToString("0.0") + " → " + after.MeanLuma.ToString("0.0"));
                    }
                }
                try
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private static Bitmap SolidBitmap(int width, int height, Color color)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(color);
            }
            return bitmap;
        }
        private static void BatchTests()
        {
            string folder = System.IO.Path.Combine(AppPaths.DataDir, "selftest-batch");
            string output = System.IO.Path.Combine(folder, "out");
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
                Directory.CreateDirectory(folder);

                string[] sources = new string[]
                {
                    AssetPath("corners.png"), AssetPath("alpha.png"), AssetPath("phone-exif.jpg")
                };
                int made = 0;
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i] == null)
                    {
                        continue;
                    }
                    string copy = System.IO.Path.Combine(folder, "src" + i + System.IO.Path.GetExtension(sources[i]));
                    File.Copy(sources[i], copy, true);
                    made++;
                }
                Check("批量：准备测试图", made > 0, "没有素材");

                int ok = 0;
                for (int i = 0; i < made; i++)
                {
                    string copy = System.IO.Path.Combine(folder, "src" + i + System.IO.Path.GetExtension(sources[i]));
                    string error;
                    if (ImageView.BatchOverlay.ExportOne(copy, output, 800, 1, out error))
                    {
                        ok++;
                    }
                }
                Check("批量：全部导出成功", ok == made, ok + "/" + made);

                string[] produced = Directory.GetFiles(output);
                Check("批量：输出数量正确", produced.Length == made, produced.Length + " 个文件");

                // 小图不放大（和调整尺寸面板一致），所以期望值是 min(800, 原长边)
                bool allSized = produced.Length > 0;
                System.Text.StringBuilder detail = new System.Text.StringBuilder();
                for (int i = 0; i < produced.Length; i++)
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(produced[i]);
                    string source = System.IO.Path.Combine(folder, "src" + name.Substring(3));
                    int expected = 800;
                    for (int k = 0; k < sources.Length; k++)
                    {
                        if (!File.Exists(System.IO.Path.Combine(folder, "src" + k + System.IO.Path.GetExtension(sources[k]))))
                        {
                            continue;
                        }
                        if (name == "src" + k)
                        {
                            using (Bitmap original = new Bitmap(System.IO.Path.Combine(folder,
                                "src" + k + System.IO.Path.GetExtension(sources[k]))))
                            {
                                expected = Math.Min(800, Math.Max(original.Width, original.Height));
                            }
                        }
                    }
                    using (Bitmap bitmap = new Bitmap(produced[i]))
                    {
                        int longSide = Math.Max(bitmap.Width, bitmap.Height);
                        if (longSide != expected)
                        {
                            allSized = false;
                        }
                        detail.Append(name).Append('=').Append(longSide).Append(' ');
                    }
                }
                Check("批量：长边不超过 800 且小图不放大", allSized, detail.ToString());
            }
            catch (Exception ex)
            {
                Check("批量：执行未抛异常", false, ex.Message);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, true);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        private static void EditGeometryTests()
        {
            // 拉直：正方形图旋转 45° 时，内接同比例矩形缩到 1/√2
            EditRecipe straighten = new EditRecipe();
            straighten.Angle = 45f;
            float scale = straighten.AutoCropScale(1000, 1000);
            Check("拉直：45° 时裁角系数约 0.707", Math.Abs(scale - 0.7071f) < 0.01f, scale.ToString("0.0000"));

            // 90° 旋转 400×300：旋转后画布 300×400，内接 4:3 矩形为 300×225
            EditRecipe quarter = new EditRecipe();
            quarter.Angle = 90f;
            using (Bitmap source = SolidBitmap(400, 300, Color.FromArgb(255, 30, 60, 90)))
            {
                using (Bitmap rotated = quarter.Rotate(source))
                {
                    Check("拉直：90° 输出尺寸", rotated != null && rotated.Width == 300 && rotated.Height == 225,
                        rotated == null ? "null" : rotated.Width + "x" + rotated.Height);
                }
            }

            // 拉直后有效尺寸同步缩小
            using (Bitmap source = SolidBitmap(400, 300, Color.FromArgb(255, 30, 60, 90)))
            using (Bitmap rotated = quarter.Rotate(source))
            {
                Rectangle crop = quarter.CropPixels(rotated.Width, rotated.Height);
                Check("拉直：裁角后仍可再裁剪", crop.Width > 0 && crop.Height > 0, crop.ToString());
            }

            // 锐化：黑白台阶边缘的对比度应提高
            using (Bitmap step = SolidBitmap(32, 32, Color.FromArgb(255, 60, 60, 60)))
            using (Graphics g = Graphics.FromImage(step))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 200, 200, 200)))
                {
                    g.FillRectangle(brush, 16, 0, 16, 32);
                }
                using (Bitmap sharpened = UnsharpMask.Apply(step, 1.0f))
                {
                    int leftEdge = sharpened.GetPixel(15, 16).R;
                    int rightEdge = sharpened.GetPixel(16, 16).R;
                    int beforeEdge = 200 - 60;
                    Check("锐化：边缘对比度增强", (rightEdge - leftEdge) > beforeEdge,
                        leftEdge + " / " + rightEdge + "（原来 60 / 200）");
                    int flat = sharpened.GetPixel(4, 16).R;
                    Check("锐化：平坦区域基本不变", Math.Abs(flat - 60) <= 2, flat.ToString());
                }
            }

            // 锐化参数上限保护
            using (Bitmap small = SolidBitmap(8, 8, Color.FromArgb(255, 10, 10, 10)))
            {
                Check("锐化：amount 0 时原样返回", ReferenceEquals(UnsharpMask.Apply(small, 0f), small), "返回了新位图");
            }
        }
        private static void HistogramTests()
        {
            using (Bitmap solid = new Bitmap(120, 80, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(solid))
                {
                    g.Clear(Color.FromArgb(255, 255, 0, 0));   // 纯红
                }
                Histogram histogram = Histogram.Compute(solid);
                Check("直方图采样数", histogram.Pixels == 120 * 80, "实际 " + histogram.Pixels);
                Check("纯红图的 R 峰值在 255", histogram.Red[255] == 120 * 80, "R[255]=" + histogram.Red[255]);
                Check("纯红图的 G/B 峰值在 0", histogram.Green[0] == 120 * 80 && histogram.Blue[0] == 120 * 80,
                    "G[0]=" + histogram.Green[0] + " B[0]=" + histogram.Blue[0]);
                Check("纯红图平均亮度约为 76", Math.Abs(histogram.MeanLuma - 76.2) < 2.5,
                    "实际 " + histogram.MeanLuma.ToString("0.0"));
                Check("纯红图无高光溢出", histogram.ClippedHigh < 0.01, histogram.ClippedHigh.ToString("0.000"));
                Check("波形网格尺寸", histogram.Wave.Length == Histogram.WaveColumns * Histogram.WaveLevels,
                    histogram.Wave.Length.ToString());
                bool waveHasData = false;
                for (int i = 0; i < histogram.Wave.Length; i++)
                {
                    if (histogram.Wave[i] > 0)
                    {
                        waveHasData = true;
                        break;
                    }
                }
                Check("波形网格有数据", waveHasData, "全为 0");
                using (Bitmap wave = histogram.RenderWaveBitmap(Color.White))
                {
                    Check("波形图可渲染", wave != null && wave.Width == Histogram.WaveColumns,
                        wave == null ? "null" : wave.Size.ToString());
                }
                histogram.Dispose();
            }

            // 过曝：纯白图应判定为高光溢出
            using (Bitmap white = new Bitmap(40, 40, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(white))
                {
                    g.Clear(Color.FromArgb(255, 255, 255, 255));
                }
                Histogram histogram = Histogram.Compute(white);
                Check("纯白图高光溢出 100%", histogram.ClippedHigh > 0.99, histogram.ClippedHigh.ToString("0.000"));
                histogram.Dispose();
            }

            // 色彩信息探测
            string jpeg = AssetPath("phone-exif.jpg");
            if (jpeg != null)
            {
                using (ImageDocument doc = ImageDocument.Open(jpeg))
                {
                    Check("EXIF 色彩空间读取", doc.Exif.ColorSpace == "sRGB", doc.Exif.ColorSpace);
                    Check("EXIF 镜头型号读取", !string.IsNullOrEmpty(doc.Exif.LensModel), "空");
                    Check("EXIF 等效焦距读取", doc.Exif.FocalLength35mm == "等效 24 mm", doc.Exif.FocalLength35mm);
                    Check("EXIF 曝光补偿读取", doc.Exif.ExposureBias == "-0.3 EV", doc.Exif.ExposureBias);
                    Check("EXIF GPS 可读", doc.Exif.HasGps, "HasGps=false");
                    Check("GPS 纬度接近 31.2189", Math.Abs(doc.Exif.GpsLatitude - 31.218913) < 0.001,
                        doc.Exif.GpsLatitude.ToString("0.000000"));
                    Check("GPS 经度接近 121.4678", Math.Abs(doc.Exif.GpsLongitude - 121.467784) < 0.001,
                        doc.Exif.GpsLongitude.ToString("0.000000"));
                    Check("GPS 海拔 12.3 m", doc.Exif.GpsAltitudeText == "12.3 m", doc.Exif.GpsAltitudeText);
                }
                ColorProfile profile = ColorProfile.Read(jpeg);
                Check("色彩配置文件探测不抛异常", profile != null, "null");
            }

            string png = AssetPath("corners.png");
            if (png != null)
            {
                ColorProfile profile = ColorProfile.Read(png);
                Check("PNG 无 ICC 时如实报告", profile != null && !profile.HasIcc, "HasIcc=true");
            }
        }
        private static void SettingsTests()
        {
            Settings original = Settings.Current;
            ThemeMode themeBackup = original.Theme;
            bool filmstripBackup = original.Filmstrip;

            Settings loaded = Settings.Load();
            Check("设置文件可读（缺失时用默认值）", loaded != null, "Load 返回 null");

            original.Theme = ThemeMode.Light;
            original.Filmstrip = false;
            original.Save();
            Settings reload = Settings.Load();
            Check("设置往返：主题", reload.Theme == ThemeMode.Light, "theme=" + reload.Theme);
            Check("设置往返：缩略图条", !reload.Filmstrip, "filmstrip=true");

            original.Theme = themeBackup;
            original.Filmstrip = filmstripBackup;
            original.Save();
        }

        private static void AssocTests()
        {
            bool wasRegistered;
            try
            {
                wasRegistered = Assoc.IsRegistered();
            }
            catch (Exception ex)
            {
                Check("读取关联状态不抛异常", false, ex.Message);
                return;
            }

            string error;
            bool ok;
            try
            {
                ok = Assoc.Register(out error);
            }
            catch (Exception ex)
            {
                Check("关联登记不抛异常", false, ex.Message);
                return;
            }

            if (!ok)
            {
                // 受限令牌（沙箱/受策略管控的账户）下注册表写入会被拒绝。
                // 此时真正要确保的是“优雅失败 + 给出原因”，而不是让程序崩掉。
                bool denied = Assoc.LastErrorType == "UnauthorizedAccessException" ||
                              Assoc.LastErrorType == "SecurityException";
                Check("受限环境下注册表写入被拒绝时优雅失败", denied, "error=" + error);
                AppLog.Line("  [SKIP] 当前会话无法写注册表，文件关联功能需在正常用户会话验证");
                return;
            }

            Check("关联登记成功", true, null);
            Check("关联登记后可检测", Assoc.IsRegistered(), "IsRegistered=false");

            bool removed = Assoc.Unregister(out error);
            Check("关联注销成功", removed, error);
            Check("注销后检测不到", !Assoc.IsRegistered(), "IsRegistered 仍为 true");

            if (wasRegistered)
            {
                string again;
                Assoc.Register(out again);
                AppLog.Line("  [INFO] 原本已登记，已恢复登记状态");
            }
        }

        /// <summary>自检结束后清掉自己产生的临时文件，保持仓库干净。</summary>
        private static void CleanupTemp()
        {
            try
            {
                string dir = TempDir();
                if (!Directory.Exists(dir))
                {
                    return;
                }
                string[] files = Directory.GetFiles(dir);
                for (int i = 0; i < files.Length; i++)
                {
                    SafeDelete(files[i]);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("CleanupTemp", ex);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
