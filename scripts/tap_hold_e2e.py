"""End-to-end test for tap-vs-hold Ctrl+V, using a real Win32 Edit control.

Unlike a screenshot-only check this proves that a quick tap reaches the target
window as a genuine native paste, while a held Ctrl+V opens the picker without
pasting anything.

Run with the app already started:
    python tap_hold_e2e.py
"""
from __future__ import annotations

import ctypes
import sys
import time
from ctypes import wintypes
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from capture_screen import capture  # noqa: E402
from hotkey_e2e import get_clipboard, set_clipboard, key  # noqa: E402

OUT = Path(__file__).parent
user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
kernel32.GetModuleHandleW.restype = wintypes.HMODULE

VK_CONTROL, VK_V, VK_DOWN = 0x11, 0x56, 0x28
KEYEVENTF_KEYUP = 0x0002
WM_SETTEXT, WM_GETTEXT, WM_DESTROY = 0x000C, 0x000D, 0x0002
PM_REMOVE = 0x0001
WS_OVERLAPPEDWINDOW, WS_VISIBLE, WS_CHILD, WS_VSCROLL = 0x00CF0000, 0x10000000, 0x40000000, 0x00200000
WS_EX_CLIENTEDGE = 0x00000200
ES_MULTILINE, ES_AUTOVSCROLL = 0x0004, 0x0040
SW_RESTORE = 9

LRESULT = ctypes.c_ssize_t
WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wintypes.HWND, wintypes.UINT,
                             wintypes.WPARAM, wintypes.LPARAM)


class WNDCLASS(ctypes.Structure):
    _fields_ = [
        ("style", wintypes.UINT),
        ("lpfnWndProc", WNDPROC),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", wintypes.HINSTANCE),
        ("hIcon", wintypes.HICON),
        ("hCursor", wintypes.HANDLE),
        ("hbrBackground", wintypes.HBRUSH),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
    ]


user32.RegisterClassW.argtypes = [ctypes.POINTER(WNDCLASS)]
user32.RegisterClassW.restype = wintypes.ATOM
user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.DefWindowProcW.restype = LRESULT
user32.CreateWindowExW.argtypes = [wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR,
                                   wintypes.DWORD, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                   ctypes.c_int, wintypes.HWND, wintypes.HMENU,
                                   wintypes.HINSTANCE, wintypes.LPVOID]
user32.CreateWindowExW.restype = wintypes.HWND
user32.SendMessageW.argtypes = [wintypes.HWND, wintypes.UINT, ctypes.c_size_t, ctypes.c_void_p]
user32.SendMessageW.restype = LRESULT
user32.GetForegroundWindow.restype = wintypes.HWND
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.SetFocus.argtypes = [wintypes.HWND]
user32.DestroyWindow.argtypes = [wintypes.HWND]
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, wintypes.LPDWORD]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
user32.PeekMessageW.argtypes = [ctypes.POINTER(wintypes.MSG), wintypes.HWND,
                                wintypes.UINT, wintypes.UINT, wintypes.UINT]
user32.DispatchMessageW.argtypes = [ctypes.POINTER(wintypes.MSG)]
user32.TranslateMessage.argtypes = [ctypes.POINTER(wintypes.MSG)]
user32.PostQuitMessage.argtypes = [ctypes.c_int]


def pump(seconds: float) -> None:
    """Run a message loop for a while, so the Edit control can process the paste."""
    msg = wintypes.MSG()
    deadline = time.monotonic() + seconds
    while True:
        while user32.PeekMessageW(ctypes.byref(msg), 0, 0, 0, PM_REMOVE):
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))
        if time.monotonic() >= deadline:
            break
        time.sleep(0.005)


def edit_text(edit: int) -> str:
    buf = ctypes.create_unicode_buffer(4096)
    user32.SendMessageW(edit, WM_GETTEXT, 4096, ctypes.c_void_p(ctypes.addressof(buf)))
    return buf.value


def clear_edit(edit: int) -> None:
    empty = ctypes.create_unicode_buffer("")
    user32.SendMessageW(edit, WM_SETTEXT, 0, ctypes.c_void_p(ctypes.addressof(empty)))


def focus(hwnd: int, edit: int) -> None:
    fg = user32.GetForegroundWindow()
    other = user32.GetWindowThreadProcessId(fg, None)
    mine = kernel32.GetCurrentThreadId()
    user32.AttachThreadInput(mine, other, True)
    # Do NOT fake an Alt press here: with a Chinese IME active it can emit a
    # stray Han character into the Edit control and corrupt the assertion.
    user32.SetForegroundWindow(hwnd)
    user32.SetFocus(edit)
    user32.AttachThreadInput(mine, other, False)
    pump(0.35)


def main() -> None:
    original_clipboard = get_clipboard()
    instance = kernel32.GetModuleHandleW(None)

    @WNDPROC
    def wnd_proc(hwnd, msg, wparam, lparam):
        if msg == WM_DESTROY:
            user32.PostQuitMessage(0)
            return 0
        return user32.DefWindowProcW(hwnd, msg, wparam, lparam)

    wc = WNDCLASS()
    wc.lpfnWndProc = wnd_proc
    wc.hInstance = instance
    wc.lpszClassName = "ClipboardWBTapHoldTest"
    if not user32.RegisterClassW(ctypes.byref(wc)):
        # 1410 = ERROR_CLASS_ALREADY_EXISTS from a crashed previous run.
        if ctypes.get_last_error() != 1410:
            raise RuntimeError("RegisterClassW failed")

    hwnd = user32.CreateWindowExW(0, wc.lpszClassName, "ClipboardWB tap/hold test",
                                  WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                                  180, 150, 560, 260, 0, 0, instance, None)
    edit = user32.CreateWindowExW(WS_EX_CLIENTEDGE, "EDIT", "",
                                  WS_CHILD | WS_VISIBLE | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL,
                                  14, 14, 510, 180, hwnd, 0, instance, None)
    if not hwnd or not edit:
        raise RuntimeError("CreateWindowExW failed")
    user32.ShowWindow(hwnd, SW_RESTORE)
    pump(0.4)                      # let the first paint/focus settle
    focus(hwnd, edit)
    user32.SetCursorPos(760, 420)

    try:
        # Two distinct entries: the newest is also what the plain clipboard holds.
        set_clipboard("AAA-FIRST-ENTRY")
        time.sleep(0.9)
        set_clipboard("BBB-SECOND-ENTRY")
        time.sleep(0.9)
        focus(hwnd, edit)

        # --- quick tap: must behave exactly like a native Ctrl+V -----------------
        key(VK_CONTROL)
        key(VK_V)
        pump(0.07)
        key(VK_V, up=True)
        key(VK_CONTROL, up=True)
        pump(0.6)
        tap_text = edit_text(edit)
        capture(str(OUT / "tap-1-short.png"))
        print(f"[tap]   edit text = {tap_text!r}")

        # --- hold: must open the picker and must NOT paste ---------------------
        clear_edit(edit)
        focus(hwnd, edit)
        key(VK_CONTROL)
        key(VK_V)
        pump(0.36)             # longer than the 180ms threshold
        key(VK_V, up=True)
        key(VK_CONTROL, up=True)
        pump(0.7)
        hold_text = edit_text(edit)
        capture(str(OUT / "hold-2-open.png"))
        print(f"[hold]  edit text = {hold_text!r}")

        # --- Down + V: paste the older entry ----------------------------------
        key(VK_DOWN)
        key(VK_DOWN, up=True)
        pump(0.25)
        key(VK_V)
        key(VK_V, up=True)
        pump(0.8)
        chosen_text = edit_text(edit)
        capture(str(OUT / "hold-3-pasted.png"))
        print(f"[paste] edit text = {chosen_text!r}")

        tap_ok = tap_text == "BBB-SECOND-ENTRY"
        hold_ok = hold_text == ""
        paste_ok = chosen_text == "AAA-FIRST-ENTRY"
        print(f"tap native paste : {'PASS' if tap_ok else 'FAIL'}")
        print(f"hold no paste    : {'PASS' if hold_ok else 'FAIL'}")
        print(f"down+V selection : {'PASS' if paste_ok else 'FAIL'}")
        print("RESULT:", "PASS" if tap_ok and hold_ok and paste_ok else "FAIL")
        print("screenshots: tap-1-short.png / hold-2-open.png / hold-3-pasted.png")
    finally:
        user32.DestroyWindow(hwnd)
        set_clipboard(original_clipboard)
        print("clipboard restored")


if __name__ == "__main__":
    main()
