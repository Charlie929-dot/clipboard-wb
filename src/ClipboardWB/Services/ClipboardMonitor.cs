using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardWB.Models;

namespace ClipboardWB.Services;

/// <summary>
/// Watches the clipboard sequence number instead of registering as a clipboard
/// viewer: no hidden native window, no viewer-chain breakage, and it works even
/// when other apps forget to broadcast WM_DRAWCLIPBOARD.
/// </summary>
internal sealed class ClipboardMonitor
{
    private readonly DispatcherTimer _timer;
    private uint _lastSeq;
    private string? _ignoreHash;
    private DateTime _suppressUntil = DateTime.MinValue;

    public event Action<ClipPayload>? Changed;

    public ClipboardMonitor(int intervalMs = 230)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        _lastSeq = NativeMethods.GetClipboardSequenceNumber();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Do not turn our own paste back into a new history entry.</summary>
    public void IgnoreOwnWrite(string hash)
    {
        _ignoreHash = hash;
        _suppressUntil = DateTime.Now.AddMilliseconds(600);
    }

    private void Tick()
    {
        uint seq = NativeMethods.GetClipboardSequenceNumber();
        if (seq == _lastSeq) return;

        ClipPayload? payload;
        try
        {
            payload = ReadClipboard();
        }
        catch
        {
            // Clipboard momentarily locked by its owner: retry on the next tick.
            return;
        }

        _lastSeq = seq;
        if (payload is null) return;

        bool ours = payload.Hash == _ignoreHash || DateTime.Now < _suppressUntil;
        if (payload.Hash == _ignoreHash) _ignoreHash = null;
        if (ours) return;

        Changed?.Invoke(payload);
    }

    /// <summary>Public so the self-test and the settings window can read it safely.</summary>
    public static ClipPayload? ReadClipboard()
    {
        if (Clipboard.ContainsFileDropList())
        {
            var list = Clipboard.GetFileDropList();
            string[] files = list.Cast<string>().Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
            if (files.Length == 0) return null;
            string joined = string.Join("\n", files);
            string first = Path.GetFileName(files[0]);
            if (string.IsNullOrWhiteSpace(first)) first = files[0];
            return new ClipPayload
            {
                Kind = ClipEntryKind.Files,
                Hash = ClipboardItem.ComputeHash(joined),
                Preview = files.Length > 1 ? $"{first} 等 {files.Length} 个文件" : first,
                Detail = $"{files.Length} 个文件",
                Files = files
            };
        }

        if (Clipboard.ContainsImage())
        {
            BitmapSource? source = Clipboard.GetImage();
            if (source is null) return null;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            byte[] png = stream.ToArray();
            if (png.Length == 0) return null;
            return new ClipPayload
            {
                Kind = ClipEntryKind.Image,
                Hash = ClipboardItem.ComputeHash(png),
                Preview = $"图片 {source.PixelWidth} × {source.PixelHeight}",
                Detail = $"{source.PixelWidth}×{source.PixelHeight} · {png.Length / 102f:0.#} KB",
                Png = png
            };
        }

        if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            string text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (string.IsNullOrEmpty(text)) return null;
            return new ClipPayload
            {
                Kind = ClipEntryKind.Text,
                Hash = ClipboardItem.ComputeHash(text),
                Preview = Collapse(text),
                Detail = $"{text.Length} 字符" + (text.Contains('\n') ? " · 多行" : ""),
                Text = text
            };
        }

        return null;
    }

    private static string Collapse(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (space) continue;
                space = true;
                sb.Append(' ');
            }
            else
            {
                space = false;
                sb.Append(c);
            }
            if (sb.Length >= 220) break;
        }
        string result = sb.ToString().Trim();
        return result.Length > 220 ? result[..220] + "…" : result;
    }
}
