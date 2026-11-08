using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace ClipboardWB.Models;

public enum ClipEntryKind
{
    Text,
    Image,
    Files
}

/// <summary>A snapshot of what was just put on the system clipboard.</summary>
public sealed class ClipPayload
{
    public ClipEntryKind Kind { get; init; }
    public string Hash { get; init; } = "";
    public string Preview { get; init; } = "";
    public string Detail { get; init; } = "";
    public string? Text { get; init; }
    public byte[]? Png { get; init; }
    public string[]? Files { get; init; }
}

public sealed class ClipboardItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ClipEntryKind Kind { get; set; }
    public string Hash { get; set; } = "";
    public string? Text { get; set; }
    public string[]? Files { get; set; }
    public string? ImageFile { get; set; }
    public string Preview { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int Index { get; set; }

    private bool _pinned;
    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value) return;
            _pinned = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore] public byte[]? Png { get; set; }
    [JsonIgnore] public BitmapImage? Thumbnail { get; set; }

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        ClipEntryKind.Image => "图片",
        ClipEntryKind.Files => "文件",
        _ => "文本"
    };

    [JsonIgnore]
    public string TimeText
    {
        get
        {
            var span = DateTime.Now - CreatedAt;
            if (span.TotalSeconds < 60) return "刚刚";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
            if (span.TotalHours < 24 && CreatedAt.Date == DateTime.Now.Date)
                return $"{(int)span.TotalHours} 小时前";
            if (span.TotalHours < 48) return "昨天 " + CreatedAt.ToString("HH:mm");
            return CreatedAt.ToString("M月d日 HH:mm");
        }
    }

    public void RefreshComputed()
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Pinned));
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(KindLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static string ComputeHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string ComputeHash(string text) => ComputeHash(Encoding.UTF8.GetBytes(text));
}
