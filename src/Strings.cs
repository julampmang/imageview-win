namespace ImageView
{
    /// <summary>所有面向用户的中文文案集中在这里，便于统一措辞与检查编码。</summary>
    internal static class Strings
    {
        // ---- 应用 ----
        internal const string AppName = "图片查看器";
        internal const string AppId = "ImageView";
        internal const string AppFileName = "ImageView.exe";
        internal const string Version = "1.0.0";

        // ---- 空态 / 错误 ----
        internal const string EmptyTitle = "拖入图片即可查看";
        internal const string EmptyHint = "也可以按 Ctrl+O 打开文件";
        internal const string DecodeFailedTitle = "无法显示这张图片";
        internal const string DecodeFailedHint = "文件可能已损坏，或缺少对应的解码器";
        internal const string UnsupportedFormat = "不支持的格式";
        internal const string FileMissing = "文件不存在或已被移动";
        internal const string Loading = "正在载入…";

        // ---- 标题栏 ----
        internal const string TipMinimize = "最小化";
        internal const string TipMaximize = "最大化";
        internal const string TipRestore = "还原";
        internal const string TipClose = "关闭";

        // ---- 工具栏 ----
        internal const string TipZoomOut = "缩小";
        internal const string TipZoomIn = "放大";
        internal const string TipFit = "适应窗口  (0)";
        internal const string TipActual = "实际大小  (1)";
        internal const string TipRotate = "旋转 90°  (R)";
        internal const string TipResize = "调整尺寸…  (Ctrl+R)";
        internal const string TipFilmstrip = "缩略图条  (Tab)";
        internal const string TipMenu = "更多";
        internal const string TipFullscreen = "全屏  (F11)";
        internal const string TipPrev = "上一张  (←)";
        internal const string TipNext = "下一张  (→)";

        // ---- 右键菜单 ----
        internal const string MenuCopy = "复制图片";
        internal const string MenuResize = "调整尺寸…";
        internal const string MenuWallpaper = "设为桌面壁纸";
        internal const string MenuReveal = "在资源管理器中显示";
        internal const string MenuInfo = "图片信息";
        internal const string MenuDelete = "删除到回收站";
        internal const string MenuDefaultApp = "设为默认打开方式…";
        internal const string MenuTheme = "外观";
        internal const string MenuThemeSystem = "跟随系统";
        internal const string MenuThemeLight = "浅色";
        internal const string MenuThemeDark = "深色";
        internal const string MenuFullscreen = "全屏";
        internal const string MenuAbout = "关于";
        internal const string MenuOpen = "打开图片…";
        internal const string MenuSettings = "设置…";

        // ---- 提示 ----
        internal const string ToastCopied = "已复制到剪贴板";
        internal const string ToastWallpaper = "已设为桌面壁纸";
        internal const string ToastDeleted = "已删除到回收站";
        internal const string ToastAssocRegistered = "已登记到「默认应用」，请在设置页选择本程序";
        internal const string ToastAssocRemoved = "已移除注册信息";
        internal const string ToastCopyFailed = "复制失败";
        internal const string ToastWallpaperFailed = "设置壁纸失败";
        internal const string ToastDeleteFailed = "删除失败，文件可能正被占用";

        // ---- 信息面板 ----
        internal const string InfoTitle = "图片信息";
        internal const string InfoName = "文件名";
        internal const string InfoPath = "位置";
        internal const string InfoSize = "尺寸";
        internal const string InfoFileSize = "文件大小";
        internal const string InfoFormat = "格式";
        internal const string InfoModified = "修改时间";
        internal const string InfoCamera = "拍摄设备";
        internal const string InfoTaken = "拍摄时间";
        internal const string InfoExposure = "曝光";
        internal const string InfoAperture = "光圈";
        internal const string InfoIso = "ISO";
        internal const string InfoFocal = "焦距";
        internal const string InfoOrient = "方向";
        internal const string InfoClose = "关闭";
        internal const string InfoZoom = "缩放";

        // ---- 调整尺寸 ----
        internal const string ResizeTitle = "调整图片尺寸";
        internal const string ResizeCurrent = "当前";
        internal const string ResizeWidth = "宽度";
        internal const string ResizeHeight = "高度";
        internal const string ResizeLockRatio = "锁定宽高比";
        internal const string ResizePercent = "按比例";
        internal const string ResizeFormat = "格式";
        internal const string ResizeQuality = "质量";
        internal const string ResizeKeepFormat = "与原图相同";
        internal const string ResizeTarget = "保存到";
        internal const string ResizeBrowse = "浏览…";
        internal const string ResizeOverwrite = "覆盖原文件";
        internal const string ResizeApply = "保存";
        internal const string ResizeCancel = "取消";
        internal const string EditApply = "应用";
        internal const string EditCancel = "取消";
        internal const string EditCrop = "裁剪…";
        internal const string EditAdjust = "调整…";
        internal const string EditSaveCopy = "另存编辑副本";
        internal const string MenuCrop = "裁剪…";
        internal const string MenuAdjust = "亮度 / 对比度…";
        internal const string MenuSaveEdited = "另存编辑副本";
        internal const string ToastEdited = "已另存编辑副本";
        internal const string ToastEditFailed = "保存失败";
        internal const string ToastNothingToSave = "当前没有编辑内容";
        internal const string BatchFolderName = "批量导出";
        internal const string MenuBatch = "批量另存…";
        internal const string ResizeRangeHint = "可输入 1 – 20000 像素";
        internal const string ResizeRotationNote = "旋转只是查看效果，不会写入保存的图片";
        internal const string ResizeSaving = "正在保存…";
        internal const string ResizeProgress = "处理中";
        internal const string ResizeDone = "已保存";
        internal const string ResizeFailed = "保存失败";
        internal const string ResizeConfirmOverwrite = "确定要覆盖原文件吗？此操作会替换 ";

        // ---- 删除确认 ----
        internal const string DeleteTitle = "删除这张图片？";
        internal const string DeleteHint = "文件会被移到回收站，可从回收站还原";
        internal const string DeleteConfirm = "删除";
        internal const string DeleteCancel = "取消";

        // ---- 设置 ----
        internal const string SettingsTitle = "设置";
        internal const string SettingsTheme = "外观主题";
        internal const string SettingsFilmstrip = "启动时显示缩略图条";
        internal const string SettingsAssoc = "文件关联";
        internal const string SettingsAssocDesc = "登记为常见图片格式的可选打开方式，不会改动你现有的默认程序";
        internal const string SettingsRegister = "登记到系统";
        internal const string SettingsUnregister = "移除登记";
        internal const string SettingsSetDefault = "打开「默认应用」设置";
        internal const string SettingsClose = "完成";
        internal const string SettingsAbout = "关于";
        internal const string SettingsPath = "配置文件";
        internal const string SettingsReset = "恢复默认外观";

        // ---- 帮助 ----
        internal const string HelpText =
            "ImageView " + Version + " —— 轻量图片查看器\r\n\r\n" +
            "用法:\r\n" +
            "  ImageView.exe [图片路径]\r\n\r\n" +
            "参数:\r\n" +
            "  --selftest   运行自检并输出结果\r\n" +
            "  --bench      运行性能基准\r\n" +
            "  --version    显示版本\r\n" +
            "  --help       显示本帮助\r\n";

        internal const string UsageOneLine = "ImageView [图片路径]";
    }
}
