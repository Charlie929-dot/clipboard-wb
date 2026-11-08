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
