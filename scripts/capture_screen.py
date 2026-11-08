"""Capture the primary screen to a PNG using ctypes/GDI only (no Pillow)."""
from __future__ import annotations

import ctypes
import struct
import sys
import zlib
from ctypes import wintypes
from pathlib import Path

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG),
        ("biHeight", wintypes.LONG), ("biPlanes", wintypes.WORD),
        ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", wintypes.LONG),
        ("biYPelsPerMeter", wintypes.LONG), ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


def png(width: int, height: int, rgba_rows) -> bytes:
    def chunk(kind, data):
        return (struct.pack(">I", len(data)) + kind + data
                + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF))

    raw = b"".join(b"\x00" + row for row in rgba_rows)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 6))
            + chunk(b"IEND", b""))


def capture(path: str) -> None:
    width = user32.GetSystemMetrics(0)
    height = user32.GetSystemMetrics(1)
    hdc = user32.GetDC(0)
    mem = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, width, height)
    gdi32.SelectObject(mem, bmp)
    gdi32.BitBlt(mem, 0, 0, width, height, hdc, 0, 0, 0x00CC0020)  # SRCCOPY

    info = BITMAPINFO()
    info.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    info.bmiHeader.biWidth = width
    info.bmiHeader.biHeight = -height  # top-down
    info.bmiHeader.biPlanes = 1
    info.bmiHeader.biBitCount = 32
    info.bmiHeader.biCompression = 0  # BI_RGB

    buf = (ctypes.c_ubyte * (width * height * 4))()
    gdi32.GetDIBits(mem, bmp, 0, height, buf, ctypes.byref(info), 0)

    rows = []
    stride = width * 4
    for y in range(height):
        chunk_bytes = bytes(buf[y * stride:(y + 1) * stride])
        row = bytearray(stride)
        row[0::4] = chunk_bytes[2::4]  # R
        row[1::4] = chunk_bytes[1::4]  # G
        row[2::4] = chunk_bytes[0::4]  # B
        row[3::4] = b"\xff" * width
        rows.append(bytes(row))

    Path(path).write_bytes(png(width, height, rows))
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(0, hdc)
    print(f"saved {path} ({width}x{height})")


if __name__ == "__main__":
    capture(sys.argv[1] if len(sys.argv) > 1 else "screen.png")
