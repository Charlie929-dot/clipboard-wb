using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardWB.Models;
using ClipboardWB.Services;
using ClipboardWB.Views;

namespace ClipboardWB;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\ClipboardWB.SingleInstance.v1";

    private Mutex? _mutex;
    private AppSettings _settings = new();
    private HistoryStore? _store;
    private ClipboardMonitor? _monitor;
    private KeyboardHook? _hook;
    private PasteService? _paste;
    private TrayService? _tray;
    private HistoryWindow? _popup;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _autoHide;
    private DispatcherTimer? _suppressReset;

    private IntPtr _targetWindow;
    private int _selected;
    private bool _demo;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "clipboardwb-error.log"),
                    args.Exception.ToString());
            }
            catch { /* ignore */ }
            args.Handled = true;
        };

        string[] args = e.Args;
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.Run();
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        _settings = AppSettings.Load();

        string? root = _demo
            ? Path.Combine(Path.GetTempPath(), "ClipboardWB.Demo")
            : null;
        _store = new HistoryStore(root, _settings.MaxItems);
        _monitor = new ClipboardMonitor();
        _paste = new PasteService();

        _popup = new HistoryWindow();
        _popup.Bind(_store);
        _popup.ItemChosen += index => { _selected = index; PasteSelected(); };
        _popup.PinToggled += item => _store.TogglePin(item);
        // Materialize the HWND once so the first popup never pays for window creation.
        _ = new System.Windows.Interop.WindowInteropHelper(_popup).EnsureHandle();

        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _autoHide.Tick += (_, _) => { _autoHide.Stop(); _popup.HidePopup(); };

        _suppressReset = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _suppressReset.Tick += (_, _) =>
        {
            _suppressReset.Stop();
            if (_hook is not null) _hook.Suppress = false;
        };

        _paste.Failed += message => _tray?.Notify("ClipboardWB", message, warning: true);
        _store.Changed += () => _tray?.SetCount(_store.Items.Count);

        _tray = new TrayService();
        _tray.SetCount(_store.Items.Count);
        _tray.SetAutoStartChecked(_settings.AutoStart);
        _tray.OpenRequested += () => ShowPopup();
        _tray.SettingsRequested += ShowSettings;
        _tray.ClearRequested += () =>
        {
            _store.Clear();
            _tray.SetCount(0);
            _tray.Notify("ClipboardWB", "历史已清空");
        };
        _tray.AutoStartChanged += enable =>
        {
            bool ok = AppSettings.SetAutoStart(enable);
            _settings.AutoStart = ok ? enable : AppSettings.IsAutoStartEnabled();
            _tray.SetAutoStartChecked(_settings.AutoStart);
            _settings.Save();
            if (!ok) _tray.Notify("ClipboardWB", "无法修改启动项", warning: true);
        };
        _tray.ExitRequested += ShutdownApp;

        if (_demo)
        {
            SeedDemoData();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var area = SystemParameters.WorkArea;
                _popup.Left = area.Left + (area.Width - _popup.Width) / 2;
                _popup.Top = area.Top + (area.Height - _popup.Height) / 2;
                _selected = 0;
                _popup.ShowPopup();
                _popup.SelectIndex(0);
            }));
            return;
        }

        _hook = new KeyboardHook { Interceptor = OnKey };
        try
        {
            _hook.Install();
        }
        catch (Exception ex)
        {
            _tray.Notify("ClipboardWB", ex.Message, warning: true);
        }

        _monitor.Changed += payload => Dispatcher.Invoke(() => _store.Add(payload));
        _monitor.Start();
    }

    private bool OnKey(KeyInfo info)
    {
        if (_popup is null || _store is null) return false;
        if (_settingsWindow is { IsVisible: true }) return false;
        bool open = _popup.IsOpen;

        switch (info.Action)
        {
            case KeyAction.CtrlV:
                if (!open) ShowPopup();
                else PasteSelected();
                return true;

            case KeyAction.PlainV:
                if (!open) return false;   // never break normal typing of the letter v
                PasteSelected();
                return true;

            case KeyAction.VRepeat:
                return open;

            case KeyAction.Up:
                if (!open) return false;
                Move(-1);
                return true;

            case KeyAction.Down:
                if (!open) return false;
                Move(1);
                return true;

            case KeyAction.Enter:
                if (!open) return false;
                PasteSelected();
                return true;

            case KeyAction.Escape:
                if (!open) return false;
                _popup.HidePopup();
                _autoHide?.Stop();
                return true;

            case KeyAction.Digit:
                if (!open) return false;
                int index = info.Digit - 1;
                if (index < _popup.ItemCount)
                {
                    _selected = index;
                    _popup.SelectIndex(index);
                    PasteSelected();
                }
                return true;

            case KeyAction.ControlUp:
                if (open) RestartAutoHide();
                return false;

            default:
                return false;
        }
    }

    private void ShowPopup()
    {
        if (_popup is null || _demo) return;
        _targetWindow = NativeMethods.GetForegroundWindow();
        _selected = 0;
        _store?.RefreshIndices();
        _popup.PositionNearCursor();
        _popup.ShowPopup();
        _popup.SelectIndex(0, animate: false); // always reopen at the top, no leftover scroll
        RestartAutoHide();
    }

    private void Move(int delta)
    {
        if (_popup is null) return;
        int count = _popup.ItemCount;
        if (count == 0) return;
        _selected = (_selected + delta + count) % count;
        _popup.SelectIndex(_selected);
        RestartAutoHide();
    }

    private void PasteSelected()
    {
        if (_popup is null || _store is null || _paste is null || _monitor is null) return;
        _autoHide?.Stop();
        _popup.HidePopup();

        var item = _store.Items.ElementAtOrDefault(Math.Clamp(_selected, 0, Math.Max(0, _store.Items.Count - 1)));
        if (item is null) return;

        if (_hook is not null)
        {
            _hook.Suppress = true;
            _suppressReset?.Start();
        }
        _paste.Paste(item, _targetWindow, _monitor);
        _store.MoveToFront(item);
    }

    private void RestartAutoHide()
    {
        if (_demo || _autoHide is null) return;
        _autoHide.Stop();
        _autoHide.Start();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _store!);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShutdownApp()
    {
        _monitor?.Stop();
        _hook?.Dispose();
        _store?.SaveNow();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Stop();
        _hook?.Dispose();
        _store?.SaveNow();
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* not owned anymore */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void SeedDemoData()
    {
        if (_store is null) return;
        _store.Add(new ClipPayload
        {
            Kind = ClipEntryKind.Image,
            Hash = ClipboardItem.ComputeHash("demo-image"),
            Preview = "图片 320 × 200",
            Detail = "320×200 · 渐变示例",
            Png = MakeDemoImage()
        });
        _store.Add(new ClipPayload
        {
            Kind = ClipEntryKind.Files,
            Hash = ClipboardItem.ComputeHash("demo-files"),
            Preview = "设计稿_v3.png 等 3 个文件",
            Detail = "3 个文件",
            Files = new[] { @"D:\Design\设计稿_v3.png", @"D:\Design\图标.svg", @"D:\Design\说明.md" }
        });
        _store.Add(new ClipPayload
        {
            Kind = ClipEntryKind.Text,
            Hash = ClipboardItem.ComputeHash("demo-3"),
            Preview = "https://github.com/Charlie929-dot/charlie_vault",
            Detail = "45 字符",
            Text = "https://github.com/Charlie929-dot/charlie_vault"
        });
        _store.Add(new ClipPayload
        {
            Kind = ClipEntryKind.Text,
            Hash = ClipboardItem.ComputeHash("demo-4"),
            Preview = "按住 Ctrl + V 唤出历史，↑ / ↓ 选择，再按 V 即可粘贴。释放 Ctrl 后面板仍会停留，可用鼠标双击或 1-9 直达。",
            Detail = "62 字符 · 多行",
            Text = "按住 Ctrl + V 唤出历史，↑ / ↓ 选择，再按 V 即可粘贴。\n释放 Ctrl 后面板仍会停留，可用鼠标双击或 1-9 直达。"
        });
        _store.Add(new ClipPayload
        {
            Kind = ClipEntryKind.Text,
            Hash = ClipboardItem.ComputeHash("demo-5"),
            Preview = "dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true",
            Detail = "86 字符",
            Text = "dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true"
        });
        _store.Add(new ClipPayload
        {
            Kind = ClipEntryKind.Text,
            Hash = ClipboardItem.ComputeHash("demo-6"),
            Preview = "周末记得把 DERP 证书续期改成 DNS-01，80 端口已经关掉了。",
            Detail = "32 字符",
            Text = "周末记得把 DERP 证书续期改成 DNS-01，80 端口已经关掉了。"
        });
    }

    private static byte[] MakeDemoImage()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(Color.FromRgb(0x9A, 0x8B, 0xFF),
                                                Color.FromRgb(0x6F, 0xD6, 0xF6), 35);
            dc.DrawRectangle(brush, null, new Rect(0, 0, 320, 200));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null,
                           new System.Windows.Point(238, 62), 42, 42);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), null,
                           new System.Windows.Point(72, 150), 62, 62);
        }
        var bitmap = new RenderTargetBitmap(320, 200, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
