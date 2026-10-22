# ImageView

[![build](https://github.com/julampmang/imageview-win/actions/workflows/build.yml/badge.svg)](https://github.com/julampmang/imageview-win/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/julampmang/imageview-win)](https://github.com/julampmang/imageview-win/releases)
[![license](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

A fast, lightweight image viewer for Windows 10/11 — a single 220 KB `.exe` with no runtime, no
installer and no dependencies. Built with the C# compiler that ships with Windows.

- **Cold start to first frame: ~133 ms**, 33 MB working set, 50 MB for an 8000×6000 JPEG
- Custom-drawn modern UI, light/dark theme, filmstrip + arrow-key navigation
- Tiered WIC decoding (≤1600 preview tier, fitted tier, 64 MB budget), GDI `StretchBlt` blitting
- EXIF (camera / lens / exposure / GPS), histogram + waveform + RGB graphs
- Non-destructive editing: crop, straighten, brightness/contrast/saturation/warmth, auto levels, USM
- Batch export, resize-with-format-detection, copy / wallpaper / reveal

> 中文文档在下方（[功能](#功能) / [构建](#构建) / [快捷键](#快捷键)）。

**Download**: see [Releases](https://github.com/julampmang/imageview-win/releases)（`ImageView-vX.Y.Z-win64.zip`）

**Build from source** (no SDK needed):
```powershell
.\build.ps1                     # -> dist\ImageView.exe
.\dist\ImageView.exe --selftest # 126 assertions
```

**License**: [MIT](LICENSE)

---

# ImageView

[![build](https://github.com/julampmang/imageview-win/actions/workflows/build.yml/badge.svg)](https://github.com/julampmang/imageview-win/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/julampmang/imageview-win)](https://github.com/julampmang/imageview-win/releases)
[![license](https://img.shields.io/badge/license-MIT-blue)](LICENSE) — Windows 10 轻量图片查看器

替代系统「照片」查看器的单文件绿色程序。用**系统自带的 C# 编译器**构建，不需要安装 .NET SDK、
不需要 MSVC、不需要任何第三方运行时。

| 指标 | 实测（Windows 10 22H2 / i5 级别机器，1180×820 窗口） |
|---|---|
| 程序体积 | **220 KB**，单文件 exe，无依赖 |
| 冷启动到首帧 | **133 ms** |
| 空窗口内存 | 工作集 33 MB / 私有 23 MB |
| 打开 8000×6000 JPEG | 解码到可用 **87 ms** |
| 看 8000×6000 大图内存 | 工作集 **50 MB** |
| 适应窗口重绘（含圆角卡片） | **14.9 ms**/帧 |
| 拖拽 / 1:1 重绘 | **7.8 ms** / **12 ms** |

对比：系统「照片」打开同一张大图通常要 1.5–3 秒、内存 150 MB 以上。

## 界面

```
┌──────────────────────────────────────────────────────────────┐
│ ▣  architecture-0321.jpg                    ⓘ  ↻ │ — □ ×     │
│    3 / 6 · 3840 × 2380 · 6.8 MB · 2026-10-20 15:08           │
│                                                              │
│  ‹        ╭──────────────────────────────────╮        ›      │
│           │                                  │               │
│           │           图片（圆角卡片）         │               │
│           │                                  │               │
│           ╰──────────────────────────────────╯               │
│              [ − 100% + │ 适应 1:1 ▤ │ ⋯ 🗑 ]                 │
│              [略缩图] [略缩图] [略缩图] [略缩图] …            │
└──────────────────────────────────────────────────────────────┘
```

- 标题栏两行：文件名 + `位置 · 尺寸 · 体积 · 修改时间`；右侧「信息 / 旋转」和「最小化 / 最大化 / 关闭」
- 翻页按钮在图片**左右两侧**（圆形，垂直居中），目录里只有一张图或全屏时自动隐藏
- 底部悬浮工具条：`缩小·比例·放大` │ `适应·1:1·缩略图` │ `更多·删除`，当前生效的档位用强调色实心块表示
- 图片完整落在视口内时，图片本身带 12px 圆角 + 1px 描边 + 一圈极淡柔光
- 文件损坏 / 格式不支持时：居中提示（文件名与失败原因在标题栏），**左右翻页和缩略图仍然可用**（这种时候最需要跳到下一张），工具条自动隐藏
- 不提供「用系统程序打开」：需要的格式请直接在本程序里解，或到资源管理器用系统的「打开方式」


## 功能

**浏览**
- 缩放（滚轮以光标为锚点）、拖拽平移、双击/`F11` 全屏
- 适应窗口、实际大小 1:1、`R` 旋转 90°
- 目录内上一张/下一张（点两侧圆形箭头或缩略图），底部缩略图条（可 `Tab` 收起）
- 只有双击**图片本身**才切全屏；点箭头、工具条、缩略图不会误触发
- 拖放文件打开，`Ctrl+O` 打开

**格式**
- JPEG / PNG / GIF / BMP / TIFF / ICO 走 WIC 主解码
- **WebP / HEIC / HEIF / AVIF** 取决于系统装了对应编解码器（本机已装 WebP + HEIF，实测可解）
- WIC 不可用时自动退回 GDI+ 解码
- 自动按 **EXIF 方向**摆正（手机竖拍照片不会躺倒）
- GIF 动图按帧延时播放，窗口最小化/隐藏时自动暂停

**调整尺寸并另存**
- 输入宽/高、锁定宽高比、25%/50%/75%/100% 快选
- 输出格式：**运行时探测**这台机器 GDI+ 能写哪些格式（JPEG/PNG/BMP/TIFF，按需带质量滑杆）
- 默认另存为 `原名-1200x800.jpg` 副本；勾选「覆盖原文件」需再点一次「确认覆盖」
- **原子写盘**：先写临时文件再替换，编码失败或目录不可写时原图保持完好
- 缩放走 WIC 解码管线，全程不产生全分辨率位图（把 8000×6000 缩到 1920 只占几十 MB）

**其它**
- 默认深色；可切浅色或「跟随系统」（`WM_SETTINGCHANGE` 实时跟随系统），见下方「切换主题」
- 缩略图条、窗口大小、主题等设置持久化到 `settings.ini`
- 右键菜单：复制图片 / 裁剪… / 亮度·对比度… / 另存编辑副本 / 调整尺寸… / 设为壁纸 / 在资源管理器中显示 / 图片信息 / 删除到回收站 / 设为默认打开方式
- 轻量编辑（`Ctrl+K` 裁剪、`Ctrl+E` 调整含拉直/锐化、`Ctrl+S` 另存）：**非破坏**，只改内存里的参数，原文件永不覆盖
- 批量另存（`Ctrl+B`）：当前目录整批按长边与格式导出到 `批量导出` 子目录
- 图片信息面板（`⋯` → 图片信息）：**直方图 + 波形图 + RGB 分量**三张图，外加完整 EXIF 与 GPS；行数多时滚轮/`↑↓` 滚动
- 界面全自绘：无原生标题栏，保留系统缩放、贴边、阴影、DPI 感知（per-monitor v2）

### 编辑为什么不会拖慢打开速度

编辑不复制像素、不解码全图，只记录一份参数：

| 操作 | 预览怎么显示 | 保存时才做的事 |
|---|---|---|
| **裁剪** | 画布换一个源矩形（`StretchBlt` 本来就支持），零像素成本 | 按矩形重采样一次 |
| **亮度 / 对比度 / 饱和度 / 色温 / 自动色阶** | 合成成**一次 `ColorMatrix`** 交给 GDI+（实测带颜色变换单帧 22.6ms，只有调整时才走这条路） | 对全图套同一个矩阵 |
| **拉直（±45°）** | 只是 GDI+ 的旋转矩阵，自动按 `min(w/(w·cos+h·sin), h/(w·sin+h·cos))` 裁掉四角，零像素成本 | 旋转一次再裁剪 |
| **锐化（USM）** | 拖动滑杆时**关掉**预览，松手后对 ≤2000 边长的预览算一次 | 保存时对全图做一次 1-2-1 可分离卷积 |
| **批量另存** | 不在看图路径上 | 逐张解码 → 缩放 → 编码，处理完立刻释放，内存峰值等于一张图 |
| 没做任何编辑时 | 完全走原来的 GDI `StretchBlt` 路径，性能与之前一致 | — |

自动色阶也是线性的（每通道按 0.6% / 99.4% 分位拉伸黑白点），所以同样能写成矩阵，
不需要 LUT、不需要额外位图。导出长边上限 4096px（约 67MB 位图），和看图时的档位预算一致。

### 信息面板里的三张图怎么看

| 图 | 横向 | 纵向 | 用来判断什么 |
|---|---|---|---|
| **直方图** | 亮度 0（暗）→ 255（亮） | 该亮度有多少像素 | 整体曝光：曲线贴左=欠曝、贴右=过曝；两端堆高=大片死黑/死白 |
| **波形图** | 画面横向位置（左→右） | 该处像素的亮度（下暗上亮），点越亮=像素越集中 | 局部曝光：哪里冲到顶就是过曝（常见于天空），哪里压到底就是死黑 |
| **RGB 分量** | 每个通道各自的色阶分布 | 像素数量 | 偏色/白平衡：红绿蓝三条整体错开，说明某个通道偏亮或偏暗 |

直方图与 RGB 分量是"分布"，波形图是"位置 + 分布"——同一张过曝的照片，直方图只会告诉你"右边堆了",
波形图能告诉你是**画面哪一块**过曝。

## 图标

一套几何，四处复用：exe 内嵌图标、任务栏/Alt+Tab、标题栏左侧标记、空状态品牌位。

- 图形：蓝色渐变圆角方块 + 白色圆角「照片」卡片 + 卡片里挖出蓝色山峦与太阳。
  小尺寸用「白卡片 + 负空间」而不是描边，16px 时轮廓最清楚。
- 唯一几何定义在 **16 单位坐标系**里：`tools/make-icon.py`（出 ico）和 `src/PaintKit.cs` 的
  `AppGlyph`（代码矢量绘制）用的是同一组坐标，改图标只需改这两处的数值。
- `assets/app.ico` 含 16/20/24/32/40/48/64/96/128/256 十档，全部 4 倍超采样后用
  **预乘 alpha** 缩放（直接缩放 RGBA 会在透明边缘振铃出白色脏像素）。

## 切换主题

默认是 **深色**。想改的话三个入口都行（选「跟随系统」后，Windows 切换深色/浅色时程序会实时跟随）：

| 入口 | 操作 | 说明 |
|---|---|---|
| 工具栏 `⋯` 菜单 | 点「外观：跟随系统 / 浅色 / 深色」这一行 | 点一次切一次，顺序：跟随系统 → 浅色 → 深色 → 跟随系统 |
| 设置面板 | `Ctrl+,` 或 `⋯` → 设置… → 「外观主题」三个选项 | 直接点目标主题 |
| 快捷键 | `Ctrl+T` | 循环切换，屏幕顶部有小提示告诉你切到了哪个 |

三种方式都会**立刻保存**到 `settings.ini`，下次启动沿用。若想回到跟随系统，切到「跟随系统」即可。





## 快捷键

| 键 | 作用 |
|---|---|
| 滚轮 | 以光标为中心缩放 |
| 拖拽 / 双击 | 平移 / 全屏 |
| `←` `→` / `PgUp` `PgDn` | 上一张 / 下一张 |
| `Home` `End` | 第一张 / 最后一张 |
| `0` | 适应窗口 |
| `1` | 实际大小 |
| `+` `-` | 放大 / 缩小 |
| `R` | 旋转 90°（仅视图） |
| `Tab` | 显示/隐藏缩略图条 |
| `F11` / `Esc` | 全屏 / 退出全屏、关闭浮层 |
| `Ctrl+T` | 切换外观（跟随系统 / 浅色 / 深色） |
| `Ctrl+K` | 裁剪 |
| `Ctrl+E` | 亮度 / 对比度 / 饱和度 / 色温 |
| `Ctrl+S` | 另存编辑副本 |
| `Ctrl+B` | 批量另存 |
| `Ctrl+R` | 调整尺寸 |
| `Ctrl+C` | 复制图片 |

| `Ctrl+O` | 打开文件 |
| `Ctrl+,` | 设置 |
| `Del` | 移到回收站 |

## 设为默认看图工具

Windows 10 **不允许程序静默抢占默认打开方式**（写 `UserChoice` 会被系统回滚），所以本程序做的是
「登记 + 引导」：

1. 打开本程序 → 右下角 `⋯` 菜单 → **设置…**
2. 点 **登记到系统**（写入 `HKCU\Software\Classes`，免管理员、可逆）
3. 点 **打开「默认应用」设置**，在设置页里按文件类型（如 `.jpg`）选择 **ImageView**
4. 想撤销：设置里点 **移除登记**

登记只会把本程序加入「打开方式」列表和「默认应用」候选，**不会改动你现有的默认程序**。

## 构建

```powershell
.\build.ps1              # 输出 dist\ImageView.exe
.\build.ps1 -Clean       # 先清空 dist 再构建
```

如果旧版本正在运行，`build.ps1` 会先把它关掉再替换（只针对输出目录里的那个 exe），不会因为文件占用而编译失败。

需要的东西：Windows 10/11 自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
（即 .NET Framework 4.8 运行时）。**仅此而已**。

源码约束：csc 支持的语言版本是 **C# 5.0**，所以代码里不用 `$""` 插值、`?.`、表达式体成员、
自动属性初始化器等新语法；源码按 UTF-8 保存并以 `/codepage:65001` 编译（`SelfTest` 里有中文文案
完整性断言，编码出问题会直接报错）。

## 弹出「SmartScreen 阻止了无法识别的应用」怎么办

这是 Windows 对**未签名程序 + 带网络来源标记**的标准拦截，不是程序有问题。两个条件同时成立才会弹：

1. **未签名**——本exe由系统自带编译器现场生成，没有购买代码签名证书；
2. **被打了来源标记**——文件经过浏览器下载、邮件附件、从网络盘/压缩包解出时，Windows 会给它加一条
   `Zone.Identifier` 备用数据流（"来自 Internet"）。

本仓库 `dist\ImageView.exe` 在 `build.ps1` 里已自动清除该标记，**从构建目录直接运行不会弹窗**。
把 exe 复制到别处（尤其是 Downloads、网盘、U 盘）后如果弹了，任选一种解除：

```powershell
# 方式一：一条命令解除（对任意副本都有效）
Unblock-File .\ImageView.exe

# 方式二：右键 exe → 属性 → 底部勾选「解除锁定」→ 确定

# 方式三：SmartScreen 弹窗里点「更多信息」→「仍要运行」（每次都点，不推荐）
```

想彻底告别这类提示（比如要发给别人）：

```powershell
# 用 PFX 证书签名
.\build.ps1 -PfxPath D:\keys\codesign.pfx -PfxPassword (Read-Host -AsSecureString)

# 或用本机证书库里的代码签名证书（CurrentUser\My）
.\build.ps1 -CertThumbprint <证书指纹>

# 离线环境跳过时间戳服务器
.\build.ps1 -CertThumbprint <指纹> -NoTimestamp
```

注意：**自签名证书只在你自己这台机器上有意义**（导入"受信任的根证书颁发机构"+"受信任的发布者"后
才不提示）。要分发给其他人、并且不出现"未知发布者"，只能买 OV/EV 代码签名证书；而且即使是正规律师
签发的证书，全新程序的 SmartScreen 声誉也需要一段时间的下载量才能累积起来。

另外：刚编译出来的未签名 exe 偶尔会被 Defender 误报（尤其是启发式扫描），可用
`Get-MpThreatDetection` 查看记录、从"保护历史记录"里还原并允许。不建议为此长期关闭实时保护或
给整个目录加白名单。

## 测试与验证

```powershell
.\dist\ImageView.exe --selftest     # 126 项断言，退出码 0 = 全通过
.\dist\ImageView.exe --bench 图片    # 启动/内存/绘制耗时基准
.\dist\ImageView.exe --probe 图片    # 单张图解码探针（尺寸/方向/alpha/EXIF）
.\run-and-capture.ps1 -File 图片 -Name 名称   # 启动并截图到 docs\screenshots
```

自检覆盖：中文编码、DPI 换算、调色板、图标绘制、格式探测、各格式解码（含 EXIF 方向=6 的像素级校验、
损坏文件、1×1 极限图）、分档缓存的升降级、画布缩放/居中/钳制/旋转、动图播放与帧内容变化、
缩放计算与编码往返、覆盖写的原子性与失败后原图完好、设置读写往返、文件关联登记/注销（受限环境下
验证优雅失败）。

`tools\paint-bench.cs` 是当初用来选渲染路径的对比程序：同一张 1600×1200 → 1116×740，
GDI+ 双线性 31.6 ms、双三次 52 ms，而 GDI `StretchBlt(HALFTONE)` 只要 3.2 ms——差一个数量级。

## 项目结构

```
build.ps1                 用系统 csc 编译 → dist\ImageView.exe
app.manifest              PerMonitorV2 DPI + Win10 兼容声明
assets\app.ico            多尺寸图标（tools\make-icon.py 生成）
src\
  Program.cs              入口、CLI、全局异常兜底
  ViewerForm.cs           主窗体：自绘外框、布局、输入路由、浮层编排
  ImageCanvas.cs (Core)   缩放/平移/旋转状态机 + 分档渲染
  Toolbar.cs              悬浮工具栏
  Filmstrip.cs            缩略图条（虚拟化）
  Overlay*.cs             菜单 / 确认 / 调整尺寸 / 设置 / 信息 浮层
  Theme.cs                明暗调色板、DPI、字体
  PaintKit.cs             圆角/阴影/文本/矢量图标
  Core\
    ImageDocument.cs      文件 → 可用位图层级（预览档/适应档/细节档）
    WicDecoder.cs         WIC COM 互操作（解码即缩放）
    GdiDecoder.cs         GDI+ 兜底解码
    GdiBlitter.cs         GDI StretchBlt 高质量快速贴图
    GdiEncoder.cs         输出格式探测与编码（原子写盘）
    ResizeJob.cs          尺寸计算 + 解码-缩放-编码编排
    GifAnimation.cs       GIF 动图帧合成与延时
    Exif.cs               自写的 EXIF/IFD 解析（方向 + 拍摄参数）
    FolderSource.cs       目录枚举与自然排序
    ThumbnailCache.cs     缩略图后台线程 + LRU
    Settings.cs / Assoc.cs / Formats.cs / Bench.cs / Probe.cs / SelfTest.cs
tools\                    图标生成、测试图生成、截图工具、渲染路径对比
tests\assets\             带可断言特征的测试图片
docs\screenshots\         各状态截图
```

## 几个关键设计

- **分档解码**：绝不为「适应窗口」去解码 8000×6000 全图。预览档（≤1600）常驻，窗口更大时按窗口
  尺寸再解一档，放大到 60% 以上且静止 120 ms 后才解细节档，且单张上限 64 MB；缩回小尺寸时大档立即释放。
  这也是内存能压在 50 MB 的原因。
- **贴图用 GDI 而非 GDI+**：GDI+ 的插值缩放实测 30–50 ms/帧，拖拽必然拖影；`StretchBlt(HALFTONE)`
  只要 3 ms，且只绘制视口内那一块（400% 放大时目标矩形可能有几万像素宽）。
- **EXIF 方向自己解析**：不依赖 WIC/GDI+ 的元数据接口，JPEG/TIFF 都能读，解码后统一摆正，
  这样缩略图、缩放输出也一定是正的。
- **受限环境兜底**：数据目录按 `%LOCALAPPDATA%\ImageView` → `程序目录\data` → `程序目录` 依次探测
  可写性；注册表/剪贴板/回收站操作全部 try-catch 并给出可见提示，绝不裸崩。

## 已知限制

- **文件关联无法在当前会话自动化验证**：本会话的进程令牌是受限令牌，写 HKCU 被系统拒绝
  （`UnauthorizedAccessException`）。代码路径已用「优雅失败」断言覆盖，但真正的登记/默认应用选择
  需要在正常用户会话里手动走一遍。程序会自动把数据目录切到 `dist\data\` 也是同一个原因。
- **HEIC/AVIF 取决于系统扩展**：没装对应解码器时会显示「无法显示这张图片」，此时可用系统的「打开方式」处理。
- **批量导出会覆盖输出目录里的同名文件**：输出目录固定为当前目录下的 `批量导出`，不放大（小图保持原尺寸）
- **锐化预览只对 ≤2000 边长的源生效**：更大的图缩放视图时预览是未锐化的，另存后才是锐化结果
- **HEIC 的拍摄信息读不到**：HEIF 把 EXIF 放在容器 `meta` box 里，本程序只解析 JPEG/TIFF/PNG/WebP 的 EXIF
  （这三种格式覆盖了绝大部分分享出来的手机照片）。HEIC 能正常显示图像，只是信息面板没有拍摄参数。
- **超大图 1:1**：为保证内存，细节档有 64 MB 上限；8000×6000 在 100% 显示时会用略低于原分辨率的
  档位放大显示（肉眼需并排对比才明显）。适应窗口/缩略图浏览不受影响。
- **GIF 帧合成**采用累加（disposal=none）语义；少数使用「还原为背景」的动图可能有残影。
- **缩放不保留 EXIF**：输出是已摆正的像素，不写入原始拍摄参数（避免缩放后 GPS/缩略图误导）。
- **旋转仅视图层**：不会写回文件；调整尺寸的输出也不包含视图旋转（按需求确认过）。
- 只出 x64 产物；目标机需 Windows 10 1803+（自带 .NET Framework 4.8）。
