using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipboardWB.Views;

/// <summary>ScrollViewer with a tiny composition-driven easing for keyboard navigation.</summary>
public sealed class SmoothScrollViewer : ScrollViewer
{
    private double _from;
    private double _to;
    private DateTime _start;
    private bool _running;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(185);

    public void AnimateTo(double target)
    {
        double clamped = Math.Clamp(target, 0, Math.Max(0, ScrollableHeight));
        if (Math.Abs(clamped - VerticalOffset) < 0.8) return;

        _from = VerticalOffset;
        _to = clamped;
        _start = DateTime.Now;
        if (!_running)
        {
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>Stop an in-flight easing so a fresh selection or a reopen can take over cleanly.</summary>
    public void CancelAnimation()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnRendering;
        _running = false;
        _to = VerticalOffset;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double t = (DateTime.Now - _start).TotalMilliseconds / Duration.TotalMilliseconds;
        if (t >= 1)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
            ScrollToVerticalOffset(_to);
            return;
        }
        double eased = 1 - Math.Pow(1 - t, 3);
        ScrollToVerticalOffset(_from + (_to - _from) * eased);
    }
}
