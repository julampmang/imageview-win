"""生成一张“手机照片级别”的测试图：完整 EXIF（IFD0 + Exif IFD + GPS IFD）。

Pillow 能写普通 tag，但无法直接写 GPS 子 IFD，所以这里手工拼 APP1：
TIFF 头 + IFD0 + Exif IFD + GPS IFD + 值区，再插到 JPEG 的 SOI 之后。

布局（小端）：
  0                 : "II" 42 offset=8
  8                 : IFD0（含 ExifIFD=0x8769 / GPS=0x8825 两个指针）
  8+s0              : Exif IFD
  8+s0+se           : GPS IFD
  8+s0+se+sg        : 值区（所有 >4 字节的内容按 IFD 顺序连续排）
"""
import os
import struct

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "tests", "assets")


# ---------- 值构造 ----------

def ascii_v(text):
    data = text.encode("ascii") + b"\x00"
    return 2, len(data), data


def short_v(v):
    return 3, 1, struct.pack("<H", v)


def long_v(v):
    return 4, 1, struct.pack("<I", v)


def rational_v(num, den=1):
    return 5, 1, struct.pack("<II", num, den)


def rationals_v(*pairs):
    return 5, len(pairs), b"".join(struct.pack("<II", n, d) for n, d in pairs)


def srational_v(num, den=1):
    return 10, 1, struct.pack("<ii", num, den)


def byte_v(v):
    return 1, 1, struct.pack("<B", v)


# ---------- IFD 组装 ----------

def ifd_size(entries):
    return 2 + 12 * len(entries) + 4


def pack_ifd(entries, values_base, values):
    """entries: [(tag, type, count, data)]；values 是共享的值区 bytearray，返回 IFD 字节。"""
    entries = sorted(entries, key=lambda e: e[0])
    out = bytearray(struct.pack("<H", len(entries)))
    for tag, typ, count, data in entries:
        out += struct.pack("<HHI", tag, typ, count)
        if len(data) <= 4:
            out += data + b"\x00" * (4 - len(data))
        else:
            if len(values) % 2:
                values.append(0)
            struct.pack_into  # noqa: B018  (仅为可读性提示)
            offset = values_base + len(values)
            out += struct.pack("<I", offset)
            values += data
    out += struct.pack("<I", 0)     # next IFD = none
    return bytes(out)


def build_app1(ifd0_entries, exif_entries, gps_entries):
    # 先补上两个子 IFD 指针（值稍后回填）
    ifd0 = list(ifd0_entries)
    ifd0.append((0x8769, 4, 1, b"\x00\x00\x00\x00"))
    ifd0.append((0x8825, 4, 1, b"\x00\x00\x00\x00"))
    ifd0.sort(key=lambda e: e[0])

    off_ifd0 = 8
    off_exif = off_ifd0 + ifd_size(ifd0)
    off_gps = off_exif + ifd_size(exif_entries)
    off_values = off_gps + ifd_size(gps_entries)

    # 回填指针
    ifd0 = [(tag, typ, cnt, struct.pack("<I", off_exif) if tag == 0x8769
             else struct.pack("<I", off_gps) if tag == 0x8825
             else data)
            for tag, typ, cnt, data in ifd0]

    values = bytearray()
    body0 = pack_ifd(ifd0, off_values, values)
    body_e = pack_ifd(exif_entries, off_values, values)
    body_g = pack_ifd(gps_entries, off_values, values)

    tiff = b"II" + struct.pack("<HI", 42, off_ifd0) + body0 + body_e + body_g + bytes(values)
    payload = b"Exif\x00\x00" + tiff
    return b"\xff\xe1" + struct.pack(">H", len(payload) + 2) + payload


def main():
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "phone-exif.jpg")

    im = Image.new("RGB", (1200, 900), (28, 32, 44))
    d = ImageDraw.Draw(im)
    for y in range(0, 900, 4):
        d.line([(0, y), (1199, y)], fill=(30 + y // 30, 40 + y // 40, 70 + y // 20))
    d.ellipse([420, 300, 780, 660], fill=(240, 210, 120))
    im.save(path, quality=92)

    ifd0 = [
        (271, *ascii_v("Apple")),
        (272, *ascii_v("iPhone 15 Pro")),
        (305, *ascii_v("17.4.1")),
        (274, *short_v(1)),
        (315, *ascii_v("Lin Tao")),
        (306, *ascii_v("2026:10:20 15:08:31")),
    ]
    exif = [
        (33434, *rational_v(1, 120)),
        (33437, *rational_v(178, 100)),
        (34855, *short_v(64)),
        (37386, *rational_v(685, 100)),
        (37383, *short_v(5)),
        (37385, *short_v(24)),
        (34850, *short_v(2)),
        (37380, *srational_v(-3, 10)),
        (40961, *short_v(1)),
        (41989, *short_v(24)),
        (41986, *short_v(0)),
        (41987, *short_v(0)),
        (41990, *short_v(0)),
        (42034, *rationals_v((678, 100), (945, 100), (178, 100), (280, 100))),
        (42036, *ascii_v("iPhone 15 Pro back triple camera 6.765mm f/1.78")),
        (36867, *ascii_v("2026:10:20 15:08:31")),
        (36868, *ascii_v("2026:10:20 15:08:31")),
    ]
    gps = [
        (1, *ascii_v("N")),
        (2, *rationals_v((31, 1), (13, 1), (8085, 1000))),
        (3, *ascii_v("E")),
        (4, *rationals_v((121, 1), (28, 1), (4021, 1000))),
        (5, *byte_v(0)),
        (6, *rational_v(1234, 100)),
        (7, *rationals_v((7, 1), (8, 1), (31, 1))),
        (29, *ascii_v("2026:10:20")),
    ]

    app1 = build_app1(ifd0, exif, gps)
    with open(path, "rb") as f:
        jpeg = f.read()
    assert jpeg[:2] == b"\xff\xd8"
    with open(path, "wb") as f:
        f.write(jpeg[:2] + app1 + jpeg[2:])

    back = Image.open(path)
    got = back.getexif()
    exif_ifd = got.get_ifd(34665)
    gps_ifd = got.get_ifd(34853)
    print("IFD0 : Make=%s Model=%s Software=%s Artist=%s"
          % (got.get(271), got.get(272), got.get(305), got.get(315)))
    print("Exif : 曝光=%s 光圈=%s ISO=%s 焦距=%s 35mm=%s"
          % (exif_ifd.get(33434), exif_ifd.get(33437), exif_ifd.get(34855),
             exif_ifd.get(37386), exif_ifd.get(41989)))
    print("Exif : 镜头=%s" % exif_ifd.get(42036))
    print("GPS  :", dict(gps_ifd))
    print("size =%d bytes" % os.path.getsize(path))


if __name__ == "__main__":
    main()
