"""Cross-window paste regression (no third-party packages).

The picker is a WS_EX_NOACTIVATE window, so it can stay open while the user
clicks into a completely different application. Pasting must land in the window
focused at confirmation time, NOT in the window that happened to open the picker.

Two real Win32 Edit controls are used:
  baseline  - open the picker in A and paste in A   -> A must receive text
  cross     - open the picker in A, switch to B, paste -> B gets text, A stays empty

Run with the app already started:
    python cross_window_e2e.py
"""
from __future__ import annotations

import ctypes
import json
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from hotkey_e2e import get_clipboard, set_clipboard, key  # noqa: E402
from tap_hold_e2e import (  # noqa: E402
    user32, kernel32, WNDPROC, WNDCLASS, pump, edit_text, clear_edit, focus,
    WS_OVERLAPPEDWINDOW, WS_VISIBLE, WS_CHILD, WS_VSCROLL, WS_EX_CLIENTEDGE,
    ES_MULTILINE, ES_AUTOVSCROLL, SW_RESTORE, WM_DESTROY, VK_CONTROL, VK_V,
)


def make_window(class_name: str, title: str, x: int, y: int):
    instance = kernel32.GetModuleHandleW(None)

    @WNDPROC
    def wnd_proc(hwnd, msg, wparam, lparam):
        if msg == WM_DESTROY:
            return 0
        return user32.DefWindowProcW(hwnd, msg, wparam, lparam)

    wc = WNDCLASS()
    wc.lpfnWndProc = wnd_proc
    wc.hInstance = instance
    wc.lpszClassName = class_name
    if not user32.RegisterClassW(ctypes.byref(wc)):
        if ctypes.get_last_error() != 1410:  # ERROR_CLASS_ALREADY_EXISTS
            raise RuntimeError("RegisterClassW failed")

    hwnd = user32.CreateWindowExW(0, wc.lpszClassName, title,
                                  WS_OVERLAPPEDWINDOW | WS_VISIBLE,
                                  x, y, 520, 230, 0, 0, instance, None)
    edit = user32.CreateWindowExW(WS_EX_CLIENTEDGE, "EDIT", "",
                                  WS_CHILD | WS_VISIBLE | WS_VSCROLL |
                                  ES_MULTILINE | ES_AUTOVSCROLL,
                                  12, 12, 480, 160, hwnd, 0, instance, None)
    if not hwnd or not edit:
        raise RuntimeError("CreateWindowExW failed")
    user32.ShowWindow(hwnd, SW_RESTORE)
    return wc, wnd_proc, hwnd, edit  # keep WNDCLASS / WNDPROC refs alive


def hold_ctrl_v() -> None:
    """Long press: opens the picker, must not paste anything."""
    key(VK_CONTROL)
    key(VK_V)
    pump(0.36)
    key(VK_V, up=True)
    key(VK_CONTROL, up=True)
    pump(0.65)


def press_v() -> None:
    """Confirm the highlighted row."""
    key(VK_V)
    key(VK_V, up=True)
    pump(0.85)


def history_size() -> int:
    path = Path(os.environ.get("APPDATA", "")) / "ClipboardWB" / "history.json"
    try:
        return len(json.loads(path.read_text(encoding="utf-8")))
    except Exception:
        return -1


def main() -> None:
    print(f"history rows available: {history_size()}")
    original_clipboard = get_clipboard()

    a = make_window("ClipboardWBCrossA", "ClipboardWB cross test - A", 120, 150)
    b = make_window("ClipboardWBCrossB", "ClipboardWB cross test - B", 700, 150)
    _, _, hwnd_a, edit_a = a
    _, _, hwnd_b, edit_b = b
    pump(0.4)

    try:
        # ---------------- baseline: open and paste in the same window ----------
        clear_edit(edit_a)
        clear_edit(edit_b)
        focus(hwnd_a, edit_a)
        hold_ctrl_v()
        press_v()
        baseline = edit_text(edit_a)
        print(f"[baseline] A = {baseline!r}")

        # ---------------- the actual regression ------------------------------
        clear_edit(edit_a)
        clear_edit(edit_b)
        focus(hwnd_a, edit_a)
        hold_ctrl_v()                       # picker opens while A is focused
        focus(hwnd_b, edit_b)               # user clicks into a different window
        foreground_is_b = user32.GetForegroundWindow() == hwnd_b
        print(f"[cross] foreground switched to B: {foreground_is_b}")
        press_v()

        a_text = edit_text(edit_a)
        b_text = edit_text(edit_b)
        print(f"[cross] A = {a_text!r}")
        print(f"[cross] B = {b_text!r}")

        baseline_ok = bool(baseline.strip())
        cross_ok = (foreground_is_b and bool(b_text.strip())
                    and a_text == "" and b_text.strip() not in {"v", "V"})
        print(f"baseline (same window) : {'PASS' if baseline_ok else 'FAIL'}")
        print(f"cross-window paste     : {'PASS' if cross_ok else 'FAIL'}")
        if not foreground_is_b:
            print("NOTE: the test harness itself failed to move focus; result is inconclusive")
        print("RESULT:", "PASS" if baseline_ok and cross_ok else "FAIL")
    finally:
        user32.DestroyWindow(hwnd_a)
        user32.DestroyWindow(hwnd_b)
        set_clipboard(original_clipboard)
        print("clipboard restored")


if __name__ == "__main__":
    main()
