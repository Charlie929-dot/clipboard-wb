using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardWB.Models;

namespace ClipboardWB.Services;

/// <summary>In-memory ordered history + debounced JSON/PNG persistence.</summary>
internal sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root;
    private readonly string _historyPath;
    private readonly string _imageDir;
    private readonly DispatcherTimer _saveTimer;

    public ObservableCollection<ClipboardItem> Items { get; } = new();

    public int MaxItems { get; set; } = 40;

    public event Action? Changed;

    public string DataDirectory => _root;

    public HistoryStore(string? root = null, int maxItems = 40)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardWB");
        _historyPath = Path.Combine(_root, "history.json");
        _imageDir = Path.Combine(_root, "images");
        MaxItems = maxItems;
        Directory.CreateDirectory(_imageDir);

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(550)
        };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        Load();
    }

    public void Add(ClipPayload payload)
    {
        var existing = Items.FirstOrDefault(i => i.Hash == payload.Hash);
        if (existing is not null)
        {
            existing.CreatedAt = DateTime.Now;
            if (!existing.Pinned)
            {
                int index = Items.IndexOf(existing);
                if (index > 0) Items.Move(index, 0);
            }
            existing.RefreshComputed();
        }
        else
        {
            var item = new ClipboardItem
            {
                Kind = payload.Kind,
                Hash = payload.Hash,
                Text = payload.Text,
                Files = payload.Files,
                Png = payload.Png,
                Preview = payload.Preview,
                Detail = payload.Detail,
                CreatedAt = DateTime.Now
            };
            if (payload.Png is not null)
            {
                item.ImageFile = Path.GetFileName(WriteImage(item.Id, payload.Png));
                item.Thumbnail = MakeThumbnail(payload.Png);
            }
            Items.Insert(0, item);
        }

        Trim();
        RefreshIndices();
        RequestSave();
        Changed?.Invoke();
    }

    public void Remove(ClipboardItem item)
    {
        if (!Items.Remove(item)) return;
        DeleteImage(item);
        RefreshIndices();
        RequestSave();
        Changed?.Invoke();
    }

    public void TogglePin(ClipboardItem item)
    {
        item.Pinned = !item.Pinned;
        item.RefreshComputed();
        RequestSave();
        Changed?.Invoke();
    }

    public void MoveToFront(ClipboardItem item)
    {
        int index = Items.IndexOf(item);
        if (index > 0) Items.Move(index, 0);
        item.CreatedAt = DateTime.Now;
        item.RefreshComputed();
        RefreshIndices();
        RequestSave();
        Changed?.Invoke();
    }

    public void Clear()
    {
        foreach (var item in Items.ToList()) DeleteImage(item);
        Items.Clear();
        RequestSave();
        Changed?.Invoke();
    }

    public void ApplyMaxItems(int max)
    {
        MaxItems = Math.Clamp(max, 5, 200);
        Trim();
        RefreshIndices();
        RequestSave();
    }

    public void RefreshIndices()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].Index = i + 1;
            Items[i].RefreshComputed();
        }
    }

    private void Trim()
    {
        while (Items.Count > MaxItems)
        {
            var last = Items.LastOrDefault(i => !i.Pinned);
            if (last is null) break;
            Items.Remove(last);
            DeleteImage(last);
        }
    }

    private string WriteImage(string id, byte[] png)
    {
        string path = Path.Combine(_imageDir, id + ".png");
        File.WriteAllBytes(path, png);
        return path;
    }

    private void DeleteImage(ClipboardItem item)
    {
        if (string.IsNullOrEmpty(item.ImageFile)) return;
        try
        {
            string path = Path.Combine(_imageDir, Path.GetFileName(item.ImageFile));
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best effort */ }
    }

    private static BitmapImage? MakeThumbnail(byte[]? png)
    {
        if (png is null || png.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(png);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 120;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(_root);
            string json = JsonSerializer.Serialize(Items.ToList(), JsonOptions);
            string temp = _historyPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _historyPath, true);
        }
        catch { /* never crash the tray app over a disk hiccup */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_historyPath)) return;
            var items = JsonSerializer.Deserialize<List<ClipboardItem>>(
                File.ReadAllText(_historyPath), JsonOptions);
            if (items is null) return;
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.ImageFile))
                {
                    string path = Path.Combine(_imageDir, Path.GetFileName(item.ImageFile));
                    if (File.Exists(path))
                    {
                        item.Png = File.ReadAllBytes(path);
                        item.Thumbnail = MakeThumbnail(item.Png);
                    }
                }
                Items.Add(item);
            }
            Trim();
            RefreshIndices();
        }
        catch { /* corrupt/older history: just start fresh */ }
    }
}
