# 参与开发

## 构建

只用**系统自带的 C# 编译器**，不需要 .NET SDK / Visual Studio：

```powershell
git clone <repo>
cd imageView
.\build.ps1                 # 输出 dist\ImageView.exe
.\build.ps1 -Clean         # 先清空 dist
```

`build.ps1` 会自动关掉正在运行的旧实例再替换文件；如果编译失败会打印 csc 的原始错误。

## 测试

```powershell
.\dist\ImageView.exe --selftest    # 126 项断言，退出码 0 表示全过
.\dist\ImageView.exe --bench .\tests\assets\big.jpg   # 性能基线
.\dist\ImageView.exe --probe .\tests\assets\phone-exif.jpg
```

提交前请至少跑一次 `--selftest`。

> `tests/assets/big.jpg`（8000×6000，1.4 MB）没有进仓库，`--bench` 前先跑
> `python tools/make-testassets.py` 生成（需要 Pillow）。EXIF 素材由
> `python tools/make-exif-asset.py` 生成，已随仓库提供。CI（`.github/workflows/build.yml`）会在 Windows runner 上
重复同样的构建 + 自检。

## 代码约定

**语言版本限制**：编译用的是 .NET Framework 4.8 自带的 csc，**只有 C# 5 语法**。不要用：

- `$"..."` 字符串插值 → 用 `string.Format`
- `?.` / `?.()` 空条件运算符
- 表达式体成员 `=>`（方法和属性都不行）
- 自动属性初始化器、`out var`、元组、`nameof`、模式匹配

**依赖限制**：不引入任何第三方 NuGet / 原生 DLL。只用 .NET Framework 4.8 自带的
`System.Drawing` / `System.Windows.Forms` / WIC（COM 互操作）。

**性能约定**：打开图片的路径上不允许新增工作。任何编辑/分析功能都必须是
"只记参数 + 预览走已解码的档位 + 保存时才付全图代价"。改完请跑 `--bench` 对比：

| 指标 | 基线 |
|---|---|
| exe → 首帧 | ≤ 140 ms |
| 空窗口工作集 | ≤ 34 MB |
| 8000×6000 打开 | ≤ 110 ms / ≤ 50 MB |
| 适应窗口每帧 | ≤ 18 ms |

**界面约定**：自绘，所有尺寸走 `Ui.S()` 做 DPI 缩放；文字宽度不要写死——相邻控件之间
的空档要用实际矩形相减算出来（历史上三次文字压按钮都是写死宽度导致的）。

## 提交 PR

1. 一个 PR 只做一件事，标题写清模块（如 `info: 修正 GPS 小数位`）。
2. 说明"改了什么 / 为什么 / 怎么验证的"，附上 `--selftest` 输出或截图。
3. 新增交互逻辑时，尽量同时加一条自检断言——这个项目的回归主要靠 126 条断言兜着。
4. 涉及界面的改动，请附上深浅两种主题的截图（`docs/screenshots/` 里加一张也行）。
