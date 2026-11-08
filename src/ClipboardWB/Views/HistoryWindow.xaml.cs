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
    private bool _dragging;
    private double _dragX, _dragY, _dragLeft, _dragTop;

    /// <summary>True while the picker is logically open (independent of fade-out).</summary>
    public bool IsOpen { get; private set; }

    public event Action<int>? ItemChosen;
    public event Action? CloseRequested;
    public event Action<ClipboardItem>? PinToggled;

    /// <summary>Raised after the user drags the header; carries the new DIP origin.</summary>
    public event Action<double, double>? PositionChanged;

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

    private double DeviceScale =>
        PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;

    /// <summary>Work area (DIPs) of the monitor containing the given DIP point.</summary>
    private (double left, double top, double right, double bottom) WorkArea(double x, double y)
    {
        double scale = DeviceScale;
        var probe = new NativeMethods.POINT
        {
            X = (int)Math.Round(x / scale),
            Y = (int)Math.Round(y / scale)
        };
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (NativeMethods.GetMonitorInfo(
                NativeMethods.MonitorFromPoint(probe, NativeMethods.MONITOR_DEFAULTTONEAREST), ref info))
            return (info.rcWork.Left * scale, info.rcWork.Top * scale,
                    info.rcWork.Right * scale, info.rcWork.Bottom * scale);

        var area = SystemParameters.WorkArea;
        return (area.Left, area.Top, area.Right, area.Bottom);
    }

    /// <summary>Position the card next to the cursor, clamped to the monitor work area.</summary>
    public void PositionNearCursor()
    {
        NativeMethods.GetCursorPos(out var point);
        double scale = DeviceScale;
        double cx = point.X * scale, cy = point.Y * scale;
        var (left, top, right, bottom) = WorkArea(cx, cy);

        double x = cx + 18;
        double y = cy - 44;
        if (x + Width > right - 12) x = cx - Width - 18;
        if (x < left + 12) x = left + 12;
        if (y + Height > bottom - 12) y = bottom - Height - 12;
        if (y < top + 12) y = top + 12;

        Left = x;
        Top = y;
    }

    /// <summary>Reuse the origin the user dragged the panel to.</summary>
    public void ApplySavedPosition(double x, double y)
    {
        Left = x;
        Top = y;
        ClampToWorkArea();
    }

    private void ClampToWorkArea()
    {
        var (left, top, right, bottom) = WorkArea(Left, Top);
        double minX = left + 12, minY = top + 12;
        Left = Math.Clamp(Left, minX, Math.Max(minX, right - Width - 12));
        Top = Math.Clamp(Top, minY, Math.Max(minY, bottom - Height - 12));
    }

    // The window is WS_EX_NOACTIVATE, so DragMove / WM_SYSCOMMAND is unreliable.
    // Track the physical cursor instead: it never drifts as the window itself moves.
    private void Header_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
        NativeMethods.GetCursorPos(out var pt);
        double scale = DeviceScale;
        _dragX = pt.X * scale;
        _dragY = pt.Y * scale;
        _dragLeft = Left;
        _dragTop = Top;
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
            }
            else
            {
                NativeMethods.GetCursorPos(out var pt);
                double scale = DeviceScale;
                Left = _dragLeft + (pt.X * scale - _dragX);
                Top = _dragTop + (pt.Y * scale - _dragY);
                e.Handled = true;
            }
        }
        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging) EndDrag(notify: true);
        base.OnPreviewMouseLeftButtonUp(e);
    }

    private void EndDrag(bool notify = false)
    {
        if (!_dragging) return;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!notify) return;
        ClampToWorkArea();
        PositionChanged?.Invoke(Left, Top);
    }

    /// <summary>
    /// Select a row and keep it inside the viewport. We deliberately do not rely on
    /// ListBox.ScrollIntoView: this window never takes focus and uses physical (pixel)
    /// scrolling, a combination where ScrollIntoView silently does nothing. Instead the
    /// target offset is derived from the row's real measured bounds and eased into place.
    /// </summary>
    public void SelectIndex(int index, bool animate = true)
    {
        if (List.Items.Count == 0) return;
        index = Math.Clamp(index, 0, List.Items.Count - 1);

        var viewer = EnsureScroller();
        if (List.SelectedIndex != index) List.SelectedIndex = index;
        if (viewer is null) return;

        viewer.CancelAnimation();
        if (!animate)
        {
            // Reopening the picker always starts from the top, with no trailing animation.
            viewer.ScrollToVerticalOffset(0);
            return;
        }

        List.UpdateLayout();
        double current = viewer.VerticalOffset;
        double? target = ComputeTargetOffset(index, viewer, current);
        if (target is null || Math.Abs(target.Value - current) < 0.5) return;
        viewer.AnimateTo(target.Value);
    }

    private double? ComputeTargetOffset(int index, SmoothScrollViewer viewer, double current)
    {
        // Coordinates relative to ItemsPresenter are content coordinates, i.e. unaffected
        // by the current scroll offset, so they can be compared with VerticalOffset directly.
        if (FindVisualChild<ItemsPresenter>(List) is not { } presenter) return null;
        if (List.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement row) return null;

        double viewport = viewer.ViewportHeight;
        if (viewport <= 0) return null;

        double top = row.TransformToAncestor(presenter).Transform(new System.Windows.Point(0, 0)).Y;
        double bottom = top + row.ActualHeight;

        const double edge = 10; // keep a little breathing room around the selected row
        double target = current;
        if (top < current + edge)
            target = top - edge;
        else if (bottom > current + viewport - edge)
            target = bottom - viewport + edge;

        return Math.Clamp(target, 0, Math.Max(0, viewer.ScrollableHeight));
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
