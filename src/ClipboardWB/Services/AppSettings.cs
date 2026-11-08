using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ClipboardWB.Services;

internal sealed class AppSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ClipboardWB";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int MaxItems { get; set; } = 40;
    public bool AutoStart { get; set; }

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardWB");

    private static string Path_ => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path_), JsonOptions);
                if (loaded is not null)
                {
                    loaded.MaxItems = Math.Clamp(loaded.MaxItems, 5, 200);
                    loaded.AutoStart = IsAutoStartEnabled();
                    return loaded;
                }
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettings { AutoStart = IsAutoStartEnabled() };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(Path_, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* non-fatal */ }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(RunValue) is string value
                   && value.Contains("ClipboardWB", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                           ?? Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null) return false;
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "";
                if (exe.Length == 0) return false;
                key.SetValue(RunValue, $"\"{exe}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(RunValue, false);
            }
            return true;
        }
        catch { return false; }
    }
}
