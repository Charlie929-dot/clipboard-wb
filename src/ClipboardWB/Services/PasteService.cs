using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardWB.Models;

namespace ClipboardWB.Services;

/// <summary>Writes the chosen entry back to the clipboard and presses Ctrl+V for the user.</summary>
internal sealed class PasteService
{
    /// <summary>Raised with a human readable reason when the paste could not be delivered.</summary>
    public event Action<string>? Failed;

    /// <summary>
    /// Write the chosen entry to the clipboard and press Ctrl+V in the window the
    /// user is actually working in. The picker is WS_EX_NOACTIVATE, so it never
    /// owns the focus: <paramref name="foreground"/> IS the paste target, even when
    /// the user switched to another window after opening the picker. The previous
    /// behaviour reactivated <paramref name="anchorWindow"/> (the window active at
    /// open time), which sent the paste back to that original window.
    /// </summary>
    public void Paste(ClipboardItem item, IntPtr anchorWindow, IntPtr foreground,
                      IntPtr popupWindow, ClipboardMonitor monitor)
    {
        // Mark our own write BEFORE touching the clipboard so the poller can
        // match the resulting sequence-number change to this hash.
        monitor.IgnoreOwnWrite(item.Hash);

        if (!WriteClipboard(item))
        {
            Failed?.Invoke("写入剪贴板失败：可能正被其他程序占用");
            return;
        }

        // Usually activate nothing: SendInput naturally reaches the live
        // foreground window. Only restore the anchor when no usable target exists.
        if (WindowToActivate(foreground, anchorWindow, popupWindow) is { } hwnd)
            NativeMethods.SetForegroundWindow(hwnd);

        var inputs = new[]
        {
            NativeMethods.MarkedKey(NativeMethods.VK_CONTROL, false),
            NativeMethods.MarkedKey(NativeMethods.VK_V, false),
            NativeMethods.MarkedKey(NativeMethods.VK_V, true),
            NativeMethods.MarkedKey(NativeMethods.VK_CONTROL, true)
        };
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs,
                                            Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
            Failed?.Invoke("模拟粘贴失败：目标窗口可能以管理员权限运行");
    }

    /// <summary>
    /// Decide whether a window must be restored to the foreground before pasting.
    /// Returns null when the live foreground window is already a valid target —
    /// in particular when it differs from the window that opened the picker.
    /// Pure so the cross-window cases are covered deterministically by --selftest.
    /// </summary>
    internal static IntPtr? WindowToActivate(IntPtr foreground, IntPtr anchor, IntPtr popup)
    {
        if (foreground != IntPtr.Zero && foreground != popup) return null;
        return anchor != IntPtr.Zero ? anchor : null;
    }

    private static bool WriteClipboard(ClipboardItem item)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                switch (item.Kind)
                {
                    case ClipEntryKind.Files:
                    {
                        var collection = new StringCollection();
                        collection.AddRange(item.Files ?? Array.Empty<string>());
                        Clipboard.SetFileDropList(collection);
                        break;
                    }
                    case ClipEntryKind.Image:
                    {
                        var source = FromPng(item.Png);
                        if (source is null) return false;
                        Clipboard.SetImage(source);
                        break;
                    }
                    default:
                    {
                        var data = new DataObject();
                        data.SetText(item.Text ?? string.Empty, TextDataFormat.UnicodeText);
                        Clipboard.SetDataObject(data, true);
                        break;
                    }
                }
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(35); // clipboard is open in another process
            }
            catch (Exception)
            {
                Thread.Sleep(35);
            }
        }
        return false;
    }

    private static BitmapSource? FromPng(byte[]? png)
    {
        if (png is null || png.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(png);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
