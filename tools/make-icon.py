"""生成 ImageView 统一图标（assets/app.ico + 预览图）。

设计（唯一几何定义，标题栏的 AppGlyph 用同一套坐标，1 单位 = size/16）：
    蓝色渐变圆角方块底  ->  白色圆角"照片"卡片  ->  卡片里挖出蓝色山峦与太阳

为什么是"白卡片 + 负空间"：小到 16px 时，白色实心块的轮廓最清楚，
而描边/细线在 16px 会被抗锯齿糊掉；负空间的山和太阳又不需要额外颜色。

所有尺寸都按 4 倍超采样绘制再降采样，保证 16/20/24 这类小尺寸不毛边。
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
ICO = os.path.join(ASSETS, "app.ico")
PREVIEW_DIR = os.path.join(ASSETS, "icon-preview")

SS = 4
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

# --- 与 src/PaintKit.cs 的 AppGlyph 保持一致的几何（16 单位坐标系） ---
BADGE_RADIUS = 3.70
CARD = (3.00, 4.30, 13.00, 11.70)
CARD_RADIUS = 1.10
SUN_CENTER = (10.50, 6.30)
SUN_RADIUS = 0.62
RIDGE = [
    (3.00, 11.70), (6.90, 6.90), (9.20, 11.70),
]
RIDGE_SMALL = [
    (6.90, 11.70), (9.60, 7.80), (12.20, 11.70),
]

TOP = (124, 156, 255)      # 渐变上端
BOTTOM = (47, 85, 200)     # 渐变下端
CARD_INK = (255, 255, 255)


def _u(v, s):
    """16 单位坐标 -> 像素坐标"""
    return v * s / 16.0


def _mix(c1, c2, t):
    return tuple(int(round(a + (b - a) * t)) for a, b in zip(c1, c2))


def draw_icon(size):
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # 渐变 + 圆角蒙版
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        gd.line([(0, y), (s, y)], fill=_mix(TOP, BOTTOM, y / max(1, s - 1)) + (255,))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(_u(BADGE_RADIUS, s)), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    card = [_u(CARD[0], s), _u(CARD[1], s), _u(CARD[2], s), _u(CARD[3], s)]
    card_radius = int(_u(CARD_RADIUS, s))

    # 白色照片卡片
    d.rounded_rectangle(card, radius=card_radius, fill=CARD_INK + (255,))

    # 卡片内的负空间图形：用卡片蒙版裁切
    holes = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    hd = ImageDraw.Draw(holes)
    hole_color = _mix(TOP, BOTTOM, 0.62) + (255,)
    hd.ellipse(
        [
            _u(SUN_CENTER[0] - SUN_RADIUS, s), _u(SUN_CENTER[1] - SUN_RADIUS, s),
            _u(SUN_CENTER[0] + SUN_RADIUS, s), _u(SUN_CENTER[1] + SUN_RADIUS, s),
        ],
        fill=hole_color,
    )
    for ridge in (RIDGE_SMALL, RIDGE):
        hd.polygon([(_u(x, s), _u(y, s)) for x, y in ridge], fill=hole_color)

    card_mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(card_mask).rounded_rectangle(card, radius=card_radius, fill=255)
    clipped = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    clipped.paste(holes, (0, 0), card_mask)
    img.alpha_composite(clipped)

    # 用预乘 alpha 缩放：直接缩放 RGBA 会让透明区域的颜色通道"振铃"出白边，
    # 边缘出现 (255,255,255,1) 这种半透明脏像素。
    return img.convert("RGBa").resize((size, size), Image.LANCZOS).convert("RGBA")


def main():
    os.makedirs(ASSETS, exist_ok=True)
    images = [draw_icon(s) for s in SIZES]
    images[-1].save(ICO, format="ICO", sizes=[(s, s) for s in SIZES])
    print("写出 %s（%d 个尺寸：%s）" % (ICO, len(SIZES), ", ".join(str(s) for s in SIZES)))

    # 预览图：单尺寸 PNG + 深浅两种背景的对比图
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    for s in (16, 24, 32, 48, 256):
        draw_icon(s).save(os.path.join(PREVIEW_DIR, "icon-%d.png" % s))

    for name, bg in (("sheet-light", (246, 246, 248)), ("sheet-dark", (19, 19, 22))):
        cell = 300
        sheet = Image.new("RGBA", (cell, 150), bg + (255,))
        x = 16
        for s, y in ((16, 40), (32, 32), (48, 22), (64, 12)):
            icon = draw_icon(s)
            sheet.alpha_composite(icon, (x, y))
            x += s + 14
        big = draw_icon(128)
        sheet.alpha_composite(big, (cell - 148, 11))
        sheet.save(os.path.join(PREVIEW_DIR, name + ".png"))
    print("预览图写到 %s" % PREVIEW_DIR)


if __name__ == "__main__":
    main()
