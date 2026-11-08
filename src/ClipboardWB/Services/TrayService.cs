using System.IO;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace ClipboardWB.Services;

/// <summary>System tray presence with a dark, flat context menu.</summary>
internal sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _autoStartItem;
    private readonly WinForms.ToolStripMenuItem _countItem;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ClearRequested;
    public event Action? ExitRequested;
    public event Action<bool>? AutoStartChanged;

    public TrayService()
    {
        var menu = new WinForms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Drawing.Font("Microsoft YaHei UI", 9f),
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColorTable()),
            BackColor = Drawing.Color.FromArgb(24, 28, 42),
            ForeColor = Drawing.Color.FromArgb(238, 240, 250),
            Padding = new WinForms.Padding(2, 5, 2, 5)
        };

        _countItem = Item("剪贴板历史", null, false);
        _countItem.Enabled = false;
        menu.Items.Add(_countItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(Item("打开历史面板    Ctrl + V", (_, _) => OpenRequested?.Invoke()));
        menu.Items.Add(Item("设置…", (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(Item("清空历史", (_, _) => ClearRequested?.Invoke()));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        _autoStartItem = Item("开机自动启动");
        _autoStartItem.Click += (_, _) =>
        {
            _autoStartItem.Checked = !_autoStartItem.Checked;
            AutoStartChanged?.Invoke(_autoStartItem.Checked);
        };
        _autoStartItem.CheckOnClick = false;
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Item("退出 ClipboardWB", (_, _) => ExitRequested?.Invoke()));

        string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        _icon = new WinForms.NotifyIcon
        {
            Icon = File.Exists(iconPath)
                ? new Drawing.Icon(iconPath)
                : Drawing.SystemIcons.Application,
            Text = "ClipboardWB · 按住 Ctrl + V 唤出历史",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
    }

    private static WinForms.ToolStripMenuItem Item(string text, EventHandler? onClick = null, bool enabled = true)
    {
        var item = new WinForms.ToolStripMenuItem(text)
        {
            ForeColor = Drawing.Color.FromArgb(232, 235, 248),
            AutoSize = true,
            Enabled = enabled,
            Padding = new WinForms.Padding(4, 3, 12, 3)
        };
        if (onClick is not null) item.Click += onClick;
        return item;
    }

    public void SetAutoStartChecked(bool value) => _autoStartItem.Checked = value;

    public void SetCount(int count) => _countItem.Text = $"已记录 {count} 条";

    public void Notify(string title, string message, bool warning = false)
    {
        try
        {
            _icon.ShowBalloonTip(2600, title, message,
                warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
        }
        catch { /* no shell available */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private sealed class DarkColorTable : WinForms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Surface = Drawing.Color.FromArgb(24, 28, 42);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(52, 48, 86);
        private static readonly Drawing.Color Edge = Drawing.Color.FromArgb(56, 62, 82);

        public override Drawing.Color MenuBorder => Edge;
        public override Drawing.Color MenuItemBorder => Drawing.Color.Transparent;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientMiddle => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Hover;
        public override Drawing.Color MenuStripGradientBegin => Surface;
        public override Drawing.Color MenuStripGradientEnd => Surface;
        public override Drawing.Color ToolStripDropDownBackground => Surface;
        public override Drawing.Color ImageMarginGradientBegin => Surface;
        public override Drawing.Color ImageMarginGradientMiddle => Surface;
        public override Drawing.Color ImageMarginGradientEnd => Surface;
        public override Drawing.Color SeparatorDark => Edge;
        public override Drawing.Color SeparatorLight => Surface;
        public override Drawing.Color CheckBackground => Hover;
        public override Drawing.Color CheckPressedBackground => Hover;
        public override Drawing.Color CheckSelectedBackground => Hover;
        public override Drawing.Color ButtonSelectedBorder => Edge;
    }
}
