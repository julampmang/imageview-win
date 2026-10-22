"""生成测试图片到 tests/assets/。

每张图都有明确可断言的视觉特征，方便自检和截图核对：
  corners.png  —— 四象限纯色（左上红/右上绿/左下蓝/右下黄），验证方向、缩放、旋转
  big.jpg      —— 8000×6000 大图（含四角色块），验证分档解码与内存
  alpha.png    —— 带透明圆的 PNG，验证棋盘格底与 alpha 判定
  anim.gif     —— 6 帧动图，验证 GIF 播放
  photo.webp   —— WebP 格式（依赖系统解码器）
  rot6.jpg     —— EXIF 方向=6 的横向图，正确显示时应变成竖图 400×600
  tiny.png     —— 1×1 极限小图
  broken.jpg   —— 非图片内容，验证错误分支
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "tests", "assets")

RED = (220, 60, 60)
GREEN = (60, 180, 90)
BLUE = (60, 100, 220)
YELLOW = (230, 190, 60)


def quadrants(w, h, pad=0):
    im = Image.new("RGB", (w, h), (30, 30, 34))
    d = ImageDraw.Draw(im)
    mx, my = w // 2, h // 2
    d.rectangle([0, 0, mx, my], fill=RED)
    d.rectangle([mx, 0, w, my], fill=GREEN)
    d.rectangle([0, my, mx, h], fill=BLUE)
    d.rectangle([mx, my, w, h], fill=YELLOW)
    if pad:
        d.rectangle([pad, pad, w - pad, h - pad], outline=(255, 255, 255), width=max(1, pad // 2))
    return im


def main():
    os.makedirs(OUT, exist_ok=True)

    p = os.path.join(OUT, "corners.png")
    quadrants(400, 300).save(p)

    p = os.path.join(OUT, "big.jpg")
    im = quadrants(8000, 6000)
    d = ImageDraw.Draw(im)
    for i in range(0, 8000, 500):
        d.line([(i, 0), (i, 6000)], fill=(255, 255, 255), width=3)
    for i in range(0, 6000, 500):
        d.line([(0, i), (8000, i)], fill=(255, 255, 255), width=3)
    im.save(p, quality=85)

    p = os.path.join(OUT, "alpha.png")
    im = Image.new("RGBA", (800, 600), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([60, 60, 740, 540], fill=(70, 140, 240, 230))
    d.ellipse([280, 180, 520, 420], fill=(250, 250, 250, 90))
    im.save(p)

    p = os.path.join(OUT, "anim.gif")
    frames = []
    for i in range(6):
        # 用 RGB 造帧再交给 Pillow 量化，避免直接操作 P 模式调色板导致全黑
        f = Image.new("RGB", (320, 240), (40 + i * 35, 70, 150 - i * 20))
        d = ImageDraw.Draw(f)
        d.rectangle([0, 0, 319, 239], outline=(255, 255, 255), width=6)
        d.ellipse([20 + i * 40, 90, 80 + i * 40, 150], fill=(255, 220, 80))
        d.text((12, 12), str(i + 1), fill=(255, 255, 255))
        frames.append(f)
    frames[0].save(p, save_all=True, append_images=frames[1:], duration=120, loop=0)

    p = os.path.join(OUT, "photo.webp")
    quadrants(800, 600).save(p, quality=85)

    p = os.path.join(OUT, "rot6.jpg")
    im = quadrants(600, 400)
    exif = Image.Exif()
    exif[274] = 6  # Orientation: 顺时针 90 度
    im.save(p, quality=90, exif=exif)

    p = os.path.join(OUT, "tiny.png")
    Image.new("RGB", (1, 1), (255, 0, 0)).save(p)

    p = os.path.join(OUT, "broken.jpg")
    with open(p, "wb") as f:
        f.write(b"this is definitely not a jpeg file\x00\x01\x02" * 20)

    for name in sorted(os.listdir(OUT)):
        full = os.path.join(OUT, name)
        print("%-14s %9d bytes" % (name, os.path.getsize(full)))


if __name__ == "__main__":
    main()
