using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipboardWB.Services;

namespace ClipboardWB.Views;

internal partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HistoryStore _store;
    private readonly DispatcherTimer _statusTimer;
    private bool _ready;

    public SettingsWindow(AppSettings settings, HistoryStore store)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusText.Text = ""; };

        // Keep the whole card on screen on small/short displays; the body scrolls.
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 36);
        if (Height > MaxHeight) Height = MaxHeight;

        Loaded += (_, _) =>
        {
            MaxSlider.Value = _settings.MaxItems;
            MaxValue.Text = _settings.MaxItems + " 条";
            AutoStartBox.IsChecked = _settings.AutoStart;
            FollowCursorBox.IsChecked = _settings.PopupFollowCursor;
            _ready = true;

            var grid = (Grid)Content;
            grid.Opacity = 0;
            CardScale.ScaleX = CardScale.ScaleY = 0.96;
            var duration = TimeSpan.FromMilliseconds(230);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            grid.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.96, 1, duration) { EasingFunction = ease });
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.96, 1, duration) { EasingFunction = ease });
        };

        // Dragging the history panel can switch the mode behind this window's back.
        Activated += (_, _) =>
        {
            if (_ready) FollowCursorBox.IsChecked = _settings.PopupFollowCursor;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                                            ref dark, sizeof(int));
        int round = 2;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                                            ref round, sizeof(int));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void MaxSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        int value = (int)e.NewValue;
        MaxValue.Text = value + " 条";
        _settings.MaxItems = value;
        _store.ApplyMaxItems(value);
        _settings.Save();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool enable = AutoStartBox.IsChecked == true;
        bool ok = AppSettings.SetAutoStart(enable);
        _settings.AutoStart = ok ? enable : AppSettings.IsAutoStartEnabled();
        AutoStartBox.IsChecked = _settings.AutoStart;
        _settings.Save();
        Status(ok ? (enable ? "已加入开机启动" : "已取消开机启动") : "没有权限修改启动项");
    }

    private void FollowCursor_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.PopupFollowCursor = FollowCursorBox.IsChecked == true;
        if (_settings.PopupFollowCursor)
        {
            _settings.PopupX = null;
            _settings.PopupY = null;
        }
        _settings.Save();
        Status(_settings.PopupFollowCursor ? "面板将跟随鼠标" : "拖动面板标题栏可固定位置");
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        int count = _store.Items.Count;
        _store.Clear();
        Status(count > 0 ? $"已清空 {count} 条记录" : "历史本来就是空的");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Status(string message)
    {
        StatusText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
