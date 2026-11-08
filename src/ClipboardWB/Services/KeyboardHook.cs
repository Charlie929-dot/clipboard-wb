using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ClipboardWB.Services;

internal enum KeyAction
{
    None,
    CtrlV,       // Ctrl+V while the picker is open -> paste the selection
    CtrlVHold,   // Ctrl+V held past the threshold -> open the picker
    PlainV,
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
///
/// Ctrl+V is tap/hold discriminated. The V keydown is held back for
/// <see cref="HoldMilliseconds"/>: releasing earlier replays a genuine native
/// Ctrl+V (so everyday pasting is untouched), holding longer opens the picker
/// and no paste is replayed. Hook callbacks and the DispatcherTimer both run on
/// the UI thread, so the state machine needs no locking.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private enum Phase { Idle, Pending, Hold }

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly DispatcherTimer _holdTimer;
    private IntPtr _handle = IntPtr.Zero;
    private Phase _phase = Phase.Idle;
    private bool _vDown;          // physical V key is currently held
    private bool _disposed;

    /// <summary>Return true to swallow the key, false to let it reach the active app.</summary>
    public Func<KeyInfo, bool>? Interceptor { get; set; }

    /// <summary>True while the picker is open; plain V/Ctrl+V then mean "paste selection".</summary>
    public Func<bool>? PickerOpen { get; set; }

    /// <summary>Global safety switch used while we synthesize our own Ctrl+V.</summary>
    public bool Suppress { get; set; }

    /// <summary>How long Ctrl+V must be held before the history picker appears.</summary>
    public int HoldMilliseconds { get; set; } = 180;

    /// <summary>Optional diagnostic sink (enabled with CLIPBOARDWB_KEYLOG).</summary>
    public Action<string>? Trace { get; set; }

    private void Log(string message) => Trace?.Invoke(message);

    public bool IsInstalled => _handle != IntPtr.Zero;

    public KeyboardHook()
    {
        _proc = HookCallback;
        _holdTimer = new DispatcherTimer(DispatcherPriority.Normal);
        _holdTimer.Tick += OnHoldElapsed;
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
                // Synthetic keys must not disturb the physical-V latch, but real
                // keys seen during a suppression window still have to update it.
                if (!ours && vk == NativeMethods.VK_V && (down || up))
                {
                    if (down) _vDown = true;
                    else FinishVRelease(replay: false, ctrlHeld: false);
                }
                return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
            }

            if (vk == NativeMethods.VK_V && (down || up) && HandleV(down, up))
                return (IntPtr)1;

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

    /// <summary>Returns true when this V event must be swallowed by the tap/hold logic.</summary>
    private bool HandleV(bool down, bool up)
    {
        bool ctrl = Mod(NativeMethods.VK_CONTROL);
        bool alt = Mod(NativeMethods.VK_MENU);
        bool shift = Mod(NativeMethods.VK_SHIFT);
        bool win = Mod(NativeMethods.VK_LWIN) || Mod(NativeMethods.VK_RWIN);

        if (up)
        {
            // A held-back keydown must not deliver an orphan keyup either.
            return FinishVRelease(replay: true, ctrlHeld: ctrl) != Phase.Idle;
        }

        if (!down) return false;
        if (_vDown) return true;                       // auto-repeat of a press we own
        _vDown = true;
        if (alt || win || shift) return false;         // leave Win+V / Ctrl+Shift+V / Alt+V alone
        if (!ctrl || PickerOpen?.Invoke() == true) return false; // let Classify handle it

        _phase = Phase.Pending;
        _holdTimer.Interval = TimeSpan.FromMilliseconds(HoldMilliseconds);
        _holdTimer.Start();
        Log($"V down -> pending ({HoldMilliseconds}ms)");
        return true;
    }

    private void OnHoldElapsed(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        // Trust our own latch, NOT GetAsyncKeyState: a swallowed keydown never
        // reaches the async key state table, so it would always read as released.
        if (_phase != Phase.Pending || !_vDown) return;

        _phase = Phase.Hold;
        // Showing a WPF window can be expensive on first use. Queue it for the
        // next dispatcher turn so the hook can keep processing keyup messages
        // instead of blocking behind the popup's layout/render.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            Interceptor?.Invoke(new KeyInfo(KeyAction.CtrlVHold));
            Log("hold -> CtrlVHold delivered");
        }));
        Log("hold -> CtrlVHold queued");
    }

    /// <summary>Close out one physical V press. Returns the phase we were in.</summary>
    private Phase FinishVRelease(bool replay, bool ctrlHeld)
    {
        Phase finished = _phase;
        _phase = Phase.Idle;
        _vDown = false;
        _holdTimer.Stop();

        bool doReplay = replay && finished == Phase.Pending;
        Log($"V up -> phase={finished}, replay={doReplay}");
        if (doReplay) SendNativePaste(ctrlHeld); // tap: replay the paste we held back
        return finished;
    }

    /// <summary>Replay the swallowed Ctrl+V so the foreground app performs its own native paste.</summary>
    private void SendNativePaste(bool ctrlHeld)
    {
        var keys = new List<NativeMethods.INPUT>(4);
        if (!ctrlHeld) keys.Add(NativeMethods.MarkedKey(NativeMethods.VK_CONTROL, false));
        keys.Add(NativeMethods.MarkedKey(NativeMethods.VK_V, false));
        keys.Add(NativeMethods.MarkedKey(NativeMethods.VK_V, true));
        if (!ctrlHeld) keys.Add(NativeMethods.MarkedKey(NativeMethods.VK_CONTROL, true));

        var inputs = keys.ToArray();
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs,
                                            Marshal.SizeOf<NativeMethods.INPUT>());
        Log($"replay native Ctrl+V (ctrlHeld={ctrlHeld}, sent={sent}/{inputs.Length})");
    }

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
        _holdTimer.Stop();
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_handle);
            _handle = IntPtr.Zero;
        }
        _phase = Phase.Idle;
    }
}
