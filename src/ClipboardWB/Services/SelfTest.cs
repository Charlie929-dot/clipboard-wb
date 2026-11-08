using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipboardWB.Models;
using ClipboardWB.Views;

namespace ClipboardWB.Services;

/// <summary>
/// Headless smoke test: <c>ClipboardWB.exe --selftest</c>. Exercises the native
/// layer, history persistence and XAML parsing without showing a UI.
/// </summary>
internal static class SelfTest
{
    public static string Run()
    {
        var lines = new List<string> { "ClipboardWB self-test " + DateTime.Now.ToString("s") };
        int failures = 0;

        void Check(string name, Func<bool> action)
        {
            try
            {
                bool ok = action();
                lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}");
                if (!ok) failures++;
            }
            catch (Exception ex)
            {
                failures++;
                lines.Add($"FAIL  {name} -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        Check("clipboard sequence number", () => NativeMethods.GetClipboardSequenceNumber() > 0);

        Check("low-level keyboard hook install/uninstall", () =>
        {
            using var hook = new KeyboardHook();
            hook.Install();
            return hook.IsInstalled;
        });

        string tempRoot = Path.Combine(Path.GetTempPath(), "ClipboardWB.SelfTest." + Guid.NewGuid().ToString("N")[..8]);
        HistoryStore? store = null;
        try
        {
            store = new HistoryStore(tempRoot, 20);
            var payload = new ClipPayload
            {
                Kind = ClipEntryKind.Text,
                Hash = ClipboardItem.ComputeHash("hello clipboard"),
                Preview = "hello clipboard",
                Detail = "16 字符",
                Text = "hello clipboard"
            };
            store.Add(payload);
            store.Add(payload);
            Check("deduplicate identical clipboard content", () => store.Items.Count == 1);

            for (int i = 0; i < 30; i++)
                store.Add(new ClipPayload
                {
                    Kind = ClipEntryKind.Text,
                    Hash = ClipboardItem.ComputeHash("item " + i),
                    Preview = "item " + i,
                    Detail = "6 字符",
                    Text = "item " + i
                });
            Check("trim history to MaxItems", () => store.Items.Count == 20);

            Check("pinned rows stay ahead of every unpinned row", () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "ClipboardWB.SelfTest.Pin");
                HistoryStore? s = null;
                try
                {
                    s = new HistoryStore(root, 20);
                    s.Add(PinPayload("A"));
                    s.Add(PinPayload("B"));
                    s.Add(PinPayload("C"));      // C, B, A
                    s.TogglePin(s.Items[2]);     // pin A   -> A, C, B
                    s.Add(PinPayload("D"));      // new row -> A, D, C, B
                    s.MoveToFront(s.Items[3]);   // use B   -> A, B, D, C
                    s.Add(PinPayload("A"));      // repeat a pinned row, must not move
                    s.TogglePin(s.Items[1]);     // pin B   -> A, B, D, C
                    s.TogglePin(s.Items[0]);     // unpin A -> B, A, D, C
                    if (!PinnedFirst(s)) return false;

                    s.SaveNow();
                    var reloaded = new HistoryStore(root, 20);
                    return reloaded.Items.Count == 4 && PinnedFirst(reloaded);
                }
                finally
                {
                    s?.Clear();
                    try { Directory.Delete(root, true); } catch { /* best effort */ }
                }
            });

            Check("trim never evicts pinned rows", () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "ClipboardWB.SelfTest.PinTrim");
                HistoryStore? s = null;
                try
                {
                    s = new HistoryStore(root, 3);
                    s.Add(PinPayload("keep"));
                    s.TogglePin(s.Items[0]);
                    for (int i = 0; i < 8; i++) s.Add(PinPayload("x" + i));
                    return s.Items.Count == 3 && s.Items[0].Pinned
                           && s.Items[0].Preview == "keep" && PinnedFirst(s);
                }
                finally
                {
                    s?.Clear();
                    try { Directory.Delete(root, true); } catch { /* best effort */ }
                }
            });

            store.Items[3].Pinned = true;
            store.SaveNow();
            var reloaded = new HistoryStore(tempRoot, 20);
            Check("persist and reload history.json", () => reloaded.Items.Count == 20);
            Check("indices are 1-based and contiguous", () =>
                reloaded.Items.Select((item, i) => item.Index == i + 1).All(ok => ok));
        }
        finally
        {
            store?.Clear();
            try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }

        Check("parse HistoryWindow XAML", () =>
        {
            var window = new HistoryWindow();
            return window is not null;
        });

        Check("parse SettingsWindow XAML", () =>
        {
            var window = new SettingsWindow(new AppSettings(), new HistoryStore(
                Path.Combine(Path.GetTempPath(), "ClipboardWB.SelfTest.UI"), 10));
            return window is not null;
        });

        // A paste must land in the window focused when the user confirms the
        // selection, not in the window that happened to open the picker.
        Check("paste target follows the live foreground window", () =>
        {
            var opened = new IntPtr(0x1111);
            var switched = new IntPtr(0x2222);
            var picker = new IntPtr(0x3333);
            return PasteService.WindowToActivate(opened, opened, picker) is null
                && PasteService.WindowToActivate(switched, opened, picker) is null
                && PasteService.WindowToActivate(IntPtr.Zero, opened, picker) == opened
                && PasteService.WindowToActivate(picker, opened, picker) == opened
                && PasteService.WindowToActivate(switched, IntPtr.Zero, picker) is null
                && PasteService.WindowToActivate(IntPtr.Zero, IntPtr.Zero, picker) is null;
        });

        Check("popup position round-trips through settings.json", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ClipboardWB.SelfTest.Pos");
            string? previous = AppSettings.OverrideDirectory;
            try
            {
                AppSettings.OverrideDirectory = root;
                new AppSettings { PopupFollowCursor = false, PopupX = 321.5, PopupY = 654.25 }.Save();
                var loaded = AppSettings.Load();
                bool saved = !loaded.PopupFollowCursor
                             && loaded.PopupX == 321.5 && loaded.PopupY == 654.25;

                // Config files written by older versions simply lack the new keys.
                File.WriteAllText(Path.Combine(root, "settings.json"), "{\"MaxItems\":40}");
                var legacy = AppSettings.Load();
                return saved && legacy.PopupFollowCursor && legacy.PopupX is null;
            }
            finally
            {
                AppSettings.OverrideDirectory = previous;
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        });

        Check("saved popup position is clamped into the work area", () =>
        {
            var window = new HistoryWindow();
            window.ApplySavedPosition(-9000, -9000);
            bool ok = window.Left >= 0 && window.Top >= 0
                      && window.Left < SystemParameters.PrimaryScreenWidth
                      && window.Top < SystemParameters.PrimaryScreenHeight;
            window.Close();
            return ok;
        });

        // The hotkey table used to be clipped by the footer with no way to scroll.
        Check("settings body reveals the last hotkey row by scrolling", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ClipboardWB.SelfTest.SettingsScroll");
            HistoryStore? s = null;
            SettingsWindow? w = null;
            try
            {
                s = new HistoryStore(root, 10);
                w = new SettingsWindow(new AppSettings(), s);
                w.Left = -3000;
                w.Top = -3000;
                w.Show();
                w.UpdateLayout();
                if (FindChild<ScrollViewer>(w) is not { } sv) return false;
                if (sv.VerticalScrollBarVisibility != ScrollBarVisibility.Auto) return false;

                double defaultOverflow = sv.ScrollableHeight;
                lines.Add($"INFO  settings content overflow at default height: {defaultOverflow:F1}px");

                // Short screen: there must be something to scroll.
                w.MaxHeight = 360;
                w.Height = 360;
                w.UpdateLayout();
                if (sv.ScrollableHeight <= 20) return false;

                // Scrolling to the end must actually bring the last row into view.
                sv.ScrollToEnd();
                w.UpdateLayout();
                var last = FindChildren<TextBlock>(w)
                    .FirstOrDefault(t => t.Text == "关闭面板，不影响原剪贴板");
                if (last is null) return false;
                double y = last.TransformToAncestor(sv).Transform(new System.Windows.Point(0, 0)).Y;
                return y >= -1 && y + last.ActualHeight <= sv.ViewportHeight + 1;
            }
            finally
            {
                w?.Close();
                s?.Clear();
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        });

        // Deterministic regression for keyboard navigation: for every jump, the offset
        // computed by SelectIndex must actually place that row inside the viewport.
        // Driving the easing animation here would need a render loop, so we apply the
        // computed target synchronously and assert against measured row bounds.
        Check("Up/Down scroll keeps the selected row in view", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ClipboardWB.SelfTest.Scroll");
            HistoryStore? scrollStore = null;
            HistoryWindow? window = null;
            try
            {
                scrollStore = new HistoryStore(root, 20);
                for (int i = 0; i < 20; i++)
                    scrollStore.Add(new ClipPayload
                    {
                        Kind = ClipEntryKind.Text,
                        Hash = ClipboardItem.ComputeHash("scroll row " + i),
                        Preview = "scroll row " + i,
                        Detail = "12 字符",
                        Text = "scroll row " + i
                    });

                window = new HistoryWindow();
                window.Bind(scrollStore);
                window.Left = -3000;   // keep the (fully transparent) window off-screen
                window.Top = -3000;
                window.Show();
                window.UpdateLayout();

                var list = window.List;
                if (FindChild<SmoothScrollViewer>(list) is not { } viewer) return false;
                if (FindChild<ItemsPresenter>(list) is not { } presenter) return false;
                if (viewer.ViewportHeight <= 0 || viewer.ScrollableHeight <= 0) return false;
                if (typeof(HistoryWindow).GetMethod("ComputeTargetOffset",
                        BindingFlags.Instance | BindingFlags.NonPublic) is not { } compute) return false;

                foreach (int index in new[] { 0, 3, 7, 11, 15, 19, 0 })
                {
                    double current = viewer.VerticalOffset;
                    if (compute.Invoke(window, new object[] { index, viewer, current }) is not double target)
                        return false;
                    viewer.ScrollToVerticalOffset(target);
                    window.UpdateLayout();

                    if (list.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement row)
                        return false;
                    double top = row.TransformToAncestor(presenter)
                                    .Transform(new System.Windows.Point(0, 0)).Y;
                    double bottom = top + row.ActualHeight;
                    double offset = viewer.VerticalOffset;
                    if (top < offset - 1 || bottom > offset + viewer.ViewportHeight + 1) return false;
                }
                return true;
            }
            finally
            {
                window?.Close();
                scrollStore?.Clear();
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        });

        try
        {
            var payload = ClipboardMonitor.ReadClipboard();
            lines.Add($"INFO  clipboard currently holds: {payload?.Kind.ToString() ?? "no supported format"}");
        }
        catch (Exception ex)
        {
            lines.Add($"INFO  clipboard read skipped: {ex.GetType().Name}");
        }

        lines.Add(failures == 0 ? "RESULT  ALL PASS" : $"RESULT  {failures} FAILED");

        string report = string.Join(Environment.NewLine, lines);
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            File.WriteAllText(path, report);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "clipboardwb-selftest.log"), report);
        }
        catch { /* still return the report */ }
        return report;
    }

    private static ClipPayload PinPayload(string name) => new()
    {
        Kind = ClipEntryKind.Text,
        Hash = ClipboardItem.ComputeHash("pin test " + name),
        Preview = name,
        Detail = "2 字符",
        Text = name
    };

    private static bool PinnedFirst(HistoryStore store)
    {
        bool seenUnpinned = false;
        foreach (var item in store.Items)
        {
            if (item.Pinned)
            {
                if (seenUnpinned) return false;
            }
            else seenUnpinned = true;
        }
        return true;
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in FindChildren<T>(child)) yield return nested;
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
