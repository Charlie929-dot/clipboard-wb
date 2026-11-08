using System.Runtime.InteropServices;

namespace ClipboardWB.Services;

internal enum KeyAction
{
    None,
    CtrlV,
    PlainV,
    VRepeat,
    Up,
    Down,
    Enter,
    Escape,
    Digit,
    ControlUp
}

internal readonly record struct KeyInfo(KeyAction Action, int Digit = 0);

/// <summary>
/// A process-wide low-level keyboard hook. It never takes focus away from the
/// target application: the popup is WS_EX_NOACTIVATE and every decision about
/// swallowing a key is delegated to <see cref="Interceptor"/>.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _handle = IntPtr.Zero;
    private bool _vHeld;
    private bool _disposed;

    /// <summary>Return true to swallow the key, false to let it reach the active app.</summary>
    public Func<KeyInfo, bool>? Interceptor { get; set; }

    /// <summary>Global safety switch used while we synthesize our own Ctrl+V.</summary>
    public bool Suppress { get; set; }

    public bool IsInstalled => _handle != IntPtr.Zero;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_handle != IntPtr.Zero) return;
        _handle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"无法安装全局键盘钩子 (Win32 error {Marshal.GetLastWin32Error()})。请确认没有被安全软件拦截。");
    }

    private static bool Mod(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == NativeMethods.HC_ACTION && lParam != IntPtr.Zero)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool ours = data.dwExtraInfo == NativeMethods.SendMagic;
            int msg = unchecked((int)wParam.ToInt64());
            bool down = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            bool up = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
            int vk = (int)data.vkCode;

            if (ours || Suppress)
            {
                // Still keep the V latch coherent so the first physical V after
                // our synthetic paste is treated as a fresh press.
                if (ours && vk == NativeMethods.VK_V)
                    _vHeld = down;
                return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
            }

            if (vk == NativeMethods.VK_V)
            {
                if (up) _vHeld = false;
                else if (down && _vHeld)
                {
                    // Auto-repeat: only meaningful while the picker is open.
                    return Swallow(new KeyInfo(KeyAction.VRepeat))
                        ? (IntPtr)1
                        : NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
                }
                else if (down) _vHeld = true;
            }

            var info = Classify(vk, down, up);
            if (info.Action != KeyAction.None)
            {
                bool swallow = Interceptor?.Invoke(info) ?? false;
                // Modifier releases must always reach the system, even if the
                // controller used them as a signal.
                if (swallow && info.Action != KeyAction.ControlUp)
                    return (IntPtr)1;
            }
        }

        return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    private bool Swallow(KeyInfo info) => Interceptor?.Invoke(info) ?? false;

    private static KeyInfo Classify(int vk, bool down, bool up)
    {
        if (up && vk is NativeMethods.VK_CONTROL or NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL)
            return new KeyInfo(KeyAction.ControlUp);
        if (!down) return default;

        bool ctrl = Mod(NativeMethods.VK_CONTROL);
        bool alt = Mod(NativeMethods.VK_MENU);
        bool shift = Mod(NativeMethods.VK_SHIFT);
        bool win = Mod(NativeMethods.VK_LWIN) || Mod(NativeMethods.VK_RWIN);

        if (vk == NativeMethods.VK_V)
        {
            if (alt || win || shift) return default;      // leave Win+V / Ctrl+Shift+V alone
            return new KeyInfo(ctrl ? KeyAction.CtrlV : KeyAction.PlainV);
        }

        if (alt || win) return default;

        return vk switch
        {
            NativeMethods.VK_UP => new KeyInfo(KeyAction.Up),
            NativeMethods.VK_DOWN => new KeyInfo(KeyAction.Down),
            NativeMethods.VK_RETURN => new KeyInfo(KeyAction.Enter),
            NativeMethods.VK_ESCAPE => new KeyInfo(KeyAction.Escape),
            >= 0x31 and <= 0x39 => new KeyInfo(KeyAction.Digit, vk - 0x30),
            >= 0x61 and <= 0x69 => new KeyInfo(KeyAction.Digit, vk - 0x60), // numpad
            _ => default
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
