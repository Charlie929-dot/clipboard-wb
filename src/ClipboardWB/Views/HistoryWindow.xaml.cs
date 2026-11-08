using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipboardWB.Models;
using ClipboardWB.Services;

namespace ClipboardWB.Views;

internal partial class HistoryWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private HistoryStore? _store;
    private SmoothScrollViewer? _scroller;

    /// <summary>True while the picker is logically open (independent of fade-out).</summary>
    public bool IsOpen { get; private set; }

    public event Action<int>? ItemChosen;
    public event Action? CloseRequested;
    public event Action<ClipboardItem>? PinToggled;

    public HistoryWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(135)
        };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                                            ref dark, sizeof(int));
        int round = 2; // DWMWCP_ROUND
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                                            ref round, sizeof(int));
    }

    public void Bind(HistoryStore store)
    {
        _store = store;
        List.ItemsSource = store.Items;
        store.Changed += RefreshMeta;
        RefreshMeta();
    }

    public int SelectedIndex => List.SelectedIndex;

    public int ItemCount => List.Items.Count;

    public void RefreshMeta()
    {
        int count = List.Items.Count;
        CountText.Text = count + " 条";
        EmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _store?.RefreshIndices();
    }

    public void ShowPopup()
    {
        _hideTimer.Stop();
        IsOpen = true;
        RefreshMeta();

        if (!IsVisible) Show();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        Animate(true);
    }

    public void HidePopup()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Animate(false);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void Animate(bool show)
    {
        Root.BeginAnimation(UIElement.OpacityProperty,
            Ease(show ? 0 : Root.Opacity, show ? 1 : 0, show ? 170 : 120));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            Ease(show ? 0.945 : RootScale.ScaleX, show ? 1 : 0.975, show ? 250 : 120));
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            Ease(show ? 0.945 : RootScale.ScaleY, show ? 1 : 0.975, show ? 250 : 120));
        RootShift.BeginAnimation(TranslateTransform.YProperty,
            Ease(show ? 16 : RootShift.Y, 0, show ? 250 : 120));
    }

    private static DoubleAnimation Ease(double from, double to, int ms) => new()
    {
        From = from,
        To = to,
        Duration = TimeSpan.FromMilliseconds(ms),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    /// <summary>Position the card next to the caret/cursor, clamped to the monitor work area.</summary>
    public void PositionNearCursor()
    {
        NativeMethods.GetCursorPos(out var point);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        bool hasMonitor = NativeMethods.GetMonitorInfo(
            NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info);

        double scale = 1.0;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            scale = source.CompositionTarget.TransformFromDevice.M11;

        double left, top, right, bottom;
        if (hasMonitor)
        {
            left = info.rcWork.Left * scale;
            top = info.rcWork.Top * scale;
            right = info.rcWork.Right * scale;
            bottom = info.rcWork.Bottom * scale;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            left = area.Left; top = area.Top; right = area.Right; bottom = area.Bottom;
        }

        double x = point.X * scale + 18;
        double y = point.Y * scale - 44;
        if (x + Width > right - 12) x = point.X * scale - Width - 18;
        if (x < left + 12) x = left + 12;
        if (y + Height > bottom - 12) y = bottom - Height - 12;
        if (y < top + 12) y = top + 12;

        Left = x;
        Top = y;
    }

    public void SelectIndex(int index)
    {
        if (List.Items.Count == 0) return;
        index = Math.Clamp(index, 0, List.Items.Count - 1);
        if (List.SelectedIndex == index) return;

        var viewer = EnsureScroller();
        double before = viewer?.VerticalOffset ?? 0;
        List.SelectedIndex = index;
        List.ScrollIntoView(List.SelectedItem);

        if (viewer is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            double after = viewer.VerticalOffset;
            if (Math.Abs(after - before) > 1)
            {
                viewer.ScrollToVerticalOffset(before);
                viewer.AnimateTo(after);
            }
        }));
    }

    private SmoothScrollViewer? EnsureScroller()
    {
        if (_scroller is not null) return _scroller;
        List.ApplyTemplate();
        _scroller = FindVisualChild<SmoothScrollViewer>(List);
        return _scroller;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static T? FindParent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        if (FindParent<Button>(origin) is not null) return; // the pin button owns its click

        var container = FindParent<ListBoxItem>(origin);
        if (container is null) return;
        int index = List.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0) return;

        SelectIndex(index);
        if (e.ClickCount >= 2 && index < List.Items.Count)
        {
            ItemChosen?.Invoke(index);
            HidePopup();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClipboardItem item }) PinToggled?.Invoke(item);
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
        HidePopup();
    }
}
