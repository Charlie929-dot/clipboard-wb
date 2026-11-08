"""Generate assets/app.ico with zero third-party dependencies.

Draws a rounded gradient tile with a clipboard + history glyph, then packs
PNG-compressed frames (16..256 px) into an ICO container.
"""
from __future__ import annotations

import struct
import zlib
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / "assets" / "app.ico"
SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 3  # supersampling factor


def blank(n: int):
    return [[(0, 0, 0, 0)] * n for _ in range(n)]


def blend(dst, x: int, y: int, color):
    n = len(dst)
    if not (0 <= x < n and 0 <= y < n):
        return
    r, g, b, a = color
    if a <= 0:
        return
    or_, og, ob, oa = dst[y][x]
    if a >= 255 or oa == 0:
        dst[y][x] = (r, g, b, max(oa, a))
        return
    fa = a / 255.0
    na = fa + (oa / 255.0) * (1 - fa)
    nr = (r * fa + or_ * (oa / 255.0) * (1 - fa)) / na
    ng = (g * fa + og * (oa / 255.0) * (1 - fa)) / na
    nb = (b * fa + ob * (oa / 255.0) * (1 - fa)) / na
    dst[y][x] = (int(nr), int(ng), int(nb), int(na * 255))


def rounded_rect(px, x0, y0, x1, y1, radius, color):
    for y in range(int(y0), int(y1)):
        for x in range(int(x0), int(x1)):
            cx = min(max(x, x0 + radius), x1 - radius - 1)
            cy = min(max(y, y0 + radius), y1 - radius - 1)
            dx, dy = x - cx, y - cy
            if dx * dx + dy * dy <= radius * radius:
                blend(px, x, y, color)


def ring(px, cx, cy, radius, width, color, start=0.0, end=1.0):
    import math

    r2, r1 = radius, radius - width
    for y in range(int(cy - r2 - 2), int(cy + r2 + 2)):
        for x in range(int(cx - r2 - 2), int(cx + r2 + 2)):
            d = math.hypot(x - cx, y - cy)
            if r1 <= d <= r2:
                ang = (math.atan2(y - cy, x - cx) + math.pi / 2) / (2 * math.pi)
                ang %= 1.0
                if start <= ang <= end:
                    blend(px, x, y, color)


def line_rect(px, x0, y0, x1, y1, color):
    rounded_rect(px, x0, y0, x1, y1, min((y1 - y0) / 2, (x1 - x0) / 2), color)


def draw(n: int):
    p = blank(n)
    u = n / 100.0  # design on a 100x100 grid

    # rounded tile + diagonal indigo/violet gradient
    for y in range(n):
        for x in range(n):
            t = (x / n * 0.65 + y / n * 0.35)
            r = int(112 - 34 * t)
            g = int(92 + 12 * t)
            b = int(244 - 12 * t)
            cx = min(max(x, 22 * u), n - 22 * u - 1)
            cy = min(max(y, 22 * u), n - 22 * u - 1)
            if (x - cx) ** 2 + (y - cy) ** 2 <= (22 * u) ** 2:
                # soft top-left glow
                glow = max(0.0, 1.0 - math_dist(x, y, n * 0.25, n * 0.15) / (n * 0.75))
                blend(p, x, y, (min(255, r + int(38 * glow)),
                                min(255, g + int(30 * glow)),
                                min(255, b + int(18 * glow)), 255))

    white = (248, 249, 255, 255)
    soft = (226, 229, 255, 205)
    # clipboard board
    rounded_rect(p, 27 * u, 24 * u, 73 * u, 79 * u, 8 * u, white)
    # clip
    rounded_rect(p, 39 * u, 17 * u, 61 * u, 31 * u, 5 * u, (126, 108, 240, 255))
    rounded_rect(p, 43 * u, 20 * u, 57 * u, 28 * u, 3 * u, (233, 231, 255, 255))
    # content lines
    line_rect(p, 35 * u, 40 * u, 65 * u, 44 * u, soft)
    line_rect(p, 35 * u, 49 * u, 60 * u, 53 * u, soft)
    line_rect(p, 35 * u, 58 * u, 64 * u, 62 * u, soft)
    # history clock in bottom-right corner
    ring(p, 70 * u, 70 * u, 15 * u, 4.2 * u, (255, 255, 255, 245), 0.0, 0.78)
    rounded_rect(p, 68 * u, 62 * u, 72 * u, 72 * u, 1.6 * u, (255, 255, 255, 245))
    line_rect(p, 70 * u, 69 * u, 78 * u, 72 * u, (255, 255, 255, 245))
    return p


def math_dist(x, y, cx, cy):
    return ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5


def downsample(p, size):
    n = len(p)
    out = []
    step = n // size
    for oy in range(size):
        row = bytearray()
        for ox in range(size):
            tr = tg = tb = ta = 0
            for dy in range(step):
                for dx in range(step):
                    r, g, b, a = p[oy * step + dy][ox * step + dx]
                    tr += r * a
                    tg += g * a
                    tb += b * a
                    ta += a
            if ta:
                row += bytes((tr // ta, tg // ta, tb // ta, ta // (step * step)))
            else:
                row += bytes((0, 0, 0, 0))
        out.append(bytes(row))
    return out


def png(size: int, rows) -> bytes:
    def chunk(kind, data):
        c = struct.pack(">I", len(data)) + kind + data
        return c + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + row for row in rows)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def main():
    frames = []
    for size in SIZES:
        big = draw(size * SS)
        frames.append((size, png(size, downsample(big, size))))
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries, blobs = b"", b""
    for size, data in frames:
        entries += struct.pack("<BBBBHHII", size if size < 256 else 0,
                               size if size < 256 else 0, 0, 0, 1, 32,
                               len(data), offset)
        blobs += data
        offset += len(data)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(header + entries + blobs)
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes, {len(frames)} frames)")


if __name__ == "__main__":
    main()
