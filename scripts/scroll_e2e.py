"""Regression screenshots for Up/Down navigation auto-scrolling.

Seeds 18 numbered history rows (well beyond one screen), opens the picker with a
real Ctrl+V, then presses Down 12 times followed by 4 Ups, capturing a screenshot
every couple of steps. Check that the highlighted ITEM-xx stays inside the card as
the list scrolls. The deterministic assertion lives in `--selftest`.

Usage (the app loads history.json at startup, so seed BEFORE launching it):
    python scroll_e2e.py --seed
    start ClipboardWB.exe
    python scroll_e2e.py
    python scroll_e2e.py --restore
"""
from __future__ import annotations

import ctypes
import json
import os
import sys
import time
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from capture_screen import capture  # noqa: E402

OUT = Path(__file__).parent
DATA = Path(os.environ["APPDATA"]) / "ClipboardWB"
HISTORY = DATA / "history.json"
BACKUP = DATA / "history.json.scrolltest-backup"

user32 = ctypes.WinDLL("user32", use_last_error=True)
KEYEVENTF_KEYUP = 0x0002
VK_CONTROL, VK_V, VK_DOWN, VK_UP, VK_ESCAPE = 0x11, 0x56, 0x28, 0x26, 0x1B


def key(vk: int, up: bool = False) -> None:
    user32.keybd_event(vk, 0, KEYEVENTF_KEYUP if up else 0, 0)
    time.sleep(0.045)


def tap(vk: int) -> None:
    key(vk)
    key(vk, up=True)


def seed(rows: int = 18) -> None:
    DATA.mkdir(parents=True, exist_ok=True)
    if HISTORY.exists() and not BACKUP.exists():  # never overwrite an existing backup
        BACKUP.write_bytes(HISTORY.read_bytes())
    items = []
    for n in range(1, rows + 1):
        label = f"ITEM-{n:02d}"
        items.append({
            "Id": f"scrolltest{n:03d}",
            "Kind": 0,
            "Hash": f"scrolltest-hash-{n:02d}",
            "Text": label + " 键盘导航自动滚动回归测试条目",
            "Files": None,
            "ImageFile": None,
            "Preview": f"{label}  键盘导航自动滚动回归测试条目",
            "Detail": "24 字符 · 文本",
            "CreatedAt": datetime.now().isoformat(timespec="seconds"),
            "Index": n,
            "Pinned": False,
        })
    HISTORY.write_text(json.dumps(items, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"seeded {rows} rows -> {HISTORY}")


def restore() -> None:
    if BACKUP.exists():
        HISTORY.write_bytes(BACKUP.read_bytes())
        BACKUP.unlink()
        print("restored original history.json")
    elif HISTORY.exists():
        HISTORY.unlink()
        print("removed seeded history.json (no backup existed)")


def main() -> None:
    if "--restore" in sys.argv:
        restore()
        return
    if "--seed" in sys.argv:
        seed()
        return

    user32.SetCursorPos(760, 420)  # fixed spot -> the popup lands in the same place
    desktop = user32.FindWindowW("Progman", None) or user32.FindWindowW("WorkerW", None)
    if desktop:
        user32.SetForegroundWindow(desktop)
    time.sleep(0.35)

    print("opening picker with Ctrl+V ...")
    key(VK_CONTROL)
    key(VK_V)
    key(VK_V, up=True)
    key(VK_CONTROL, up=True)
    time.sleep(0.7)
    capture(str(OUT / "scroll-step00.png"))

    for step in range(1, 13):
        tap(VK_DOWN)
        time.sleep(0.29)  # let the 185ms easing settle
        if step % 2 == 0:
            capture(str(OUT / f"scroll-step{step:02d}.png"))

    for _ in range(4):
        tap(VK_UP)
        time.sleep(0.29)
    capture(str(OUT / "scroll-step-up.png"))

    tap(VK_ESCAPE)
    print("screenshots written to", OUT)


if __name__ == "__main__":
    main()
