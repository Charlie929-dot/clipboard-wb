using System.IO;
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
}
