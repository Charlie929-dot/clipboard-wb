"""End-to-end hotkey test with no third-party packages.

Seeds two clipboard entries, focuses the desktop, then drives the real
Ctrl+V -> Down -> V flow through SendInput and screenshots each stage.
"""
from __future__ import annotations

import ctypes
import sys
import time
from ctypes import wintypes
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from capture_screen import capture  # noqa: E402

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

CF_UNICODETEXT = 13
GMEM_MOVEABLE = 0x0002
KEYEVENTF_KEYUP = 0x0002
VK_CONTROL, VK_V, VK_DOWN = 0x11, 0x56, 0x28

kernel32.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
kernel32.GlobalAlloc.restype = wintypes.HGLOBAL
kernel32.GlobalLock.argtypes = [wintypes.HGLOBAL]
kernel32.GlobalLock.restype = ctypes.c_void_p
kernel32.GlobalUnlock.argtypes = [wintypes.HGLOBAL]
user32.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
user32.SetClipboardData.restype = wintypes.HANDLE
user32.GetClipboardData.argtypes = [wintypes.UINT]
user32.GetClipboardData.restype = wintypes.HANDLE
user32.OpenClipboard.argtypes = [wintypes.HWND]
user32.FindWindowW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR]
user32.FindWindowW.restype = wintypes.HWND


def set_clipboard(text: str) -> None:
    for _ in range(10):
        if user32.OpenClipboard(0):
            break
        time.sleep(0.05)
    else:
        raise RuntimeError("cannot open clipboard")
    user32.EmptyClipboard()
    payload = text.encode("utf-16-le") + b"\x00\x00"
    handle = kernel32.GlobalAlloc(GMEM_MOVEABLE, len(payload))
    ptr = kernel32.GlobalLock(handle)
    ctypes.memmove(ptr, payload, len(payload))
    kernel32.GlobalUnlock(handle)
    user32.SetClipboardData(CF_UNICODETEXT, handle)
    user32.CloseClipboard()


def get_clipboard() -> str:
    for _ in range(10):
        if user32.OpenClipboard(0):
            break
        time.sleep(0.05)
    else:
        return "<locked>"
    handle = user32.GetClipboardData(CF_UNICODETEXT)
    value = ctypes.wstring_at(kernel32.GlobalLock(handle)) if handle else ""
    if handle:
        kernel32.GlobalUnlock(handle)
    user32.CloseClipboard()
    return value


def key(vk: int, up: bool = False) -> None:
    user32.keybd_event(vk, 0, KEYEVENTF_KEYUP if up else 0, 0)
    time.sleep(0.045)


def combo_ctrl_v() -> None:
    key(VK_CONTROL)
    key(VK_V)
    key(VK_V, up=True)
    key(VK_CONTROL, up=True)


def main() -> None:
    out = Path(__file__).parent
    print("seeding clipboard history ...")
    set_clipboard("AAA-FIRST-ENTRY")
    time.sleep(0.9)
    set_clipboard("BBB-SECOND-ENTRY")
    time.sleep(0.9)

    desktop = user32.FindWindowW("Progman", None) or user32.FindWindowW("WorkerW", None)
    if desktop:
        user32.SetForegroundWindow(desktop)
        time.sleep(0.2)
    print("foreground hwnd:", user32.GetForegroundWindow())

    print("step 1: Ctrl+V should open the picker")
    combo_ctrl_v()
    time.sleep(0.7)
    capture(str(out / "e2e-1-open.png"))

    print("step 2: Down should move selection to the second item")
    key(VK_DOWN)
    key(VK_DOWN, up=True)
    time.sleep(0.5)
    capture(str(out / "e2e-2-down.png"))

    print("step 3: V should paste the selected (older) item")
    key(VK_V)
    key(VK_V, up=True)
    time.sleep(0.8)
    capture(str(out / "e2e-3-pasted.png"))

    result = get_clipboard()
    print("clipboard after paste:", repr(result))
    print("RESULT:", "PASS" if result == "AAA-FIRST-ENTRY" else "FAIL")


if __name__ == "__main__":
    main()
