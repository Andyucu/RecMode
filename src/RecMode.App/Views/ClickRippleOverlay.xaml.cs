using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RecMode.Capture;

namespace RecMode.App.Views;

/// <summary>
/// Fullscreen click-highlight overlay (plan Phase 8): draws an expanding, fading ring at each mouse click.
/// Click-through (WS_EX_TRANSPARENT) so it never intercepts input, non-activating, and deliberately NOT
/// excluded from capture so the ripple is part of the recording. Sized to whatever is actually being recorded
/// (same resolution <see cref="AnnotationOverlay"/> already uses), not just the primary monitor — without
/// this, recording a non-primary display with "Highlight mouse clicks" on silently produced no ripples at all:
/// <see cref="AddRipple"/> only draws for clicks inside <see cref="_bounds"/>, which every click on the actual
/// recorded (non-primary) monitor would fail.
/// </summary>
public partial class ClickRippleOverlay : Window
{
    private readonly RegionRect _bounds;
    private double _dpiScale = 1.0;

    public ClickRippleOverlay(CaptureTarget? target)
    {
        InitializeComponent();
        if (target is not null && CaptureCapabilities.TryGetScreenBounds(target, out RegionRect bounds))
        {
            _bounds = bounds;
        }
        else
        {
            // Null when the session has no attached display at all (RDP/console disconnect) — fall back to an
            // empty rect so the overlay is simply invisible, rather than throwing out of a constructor that
            // runs on the UI thread during recording start.
            MonitorInfo? mon = CaptureCapabilities.PrimaryOrFirstMonitor();
            _bounds = mon is null ? default : new RegionRect(mon.X, mon.Y, mon.Width, mon.Height);
        }

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        OverlayWindowStyle.ApplyClickThrough(hwnd);
        OverlayWindowStyle.SetBounds(hwnd, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget? ct = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (ct is not null)
        {
            _dpiScale = ct.TransformToDevice.M11;
        }
    }

    /// <summary>Adds a ripple at the given screen (physical, virtual-desktop) coordinates.</summary>
    public void AddRipple(int screenX, int screenY)
    {
        // Only ripple clicks on the covered area.
        if (screenX < _bounds.X || screenX >= _bounds.X + _bounds.Width ||
            screenY < _bounds.Y || screenY >= _bounds.Y + _bounds.Height)
        {
            return;
        }

        double x = (screenX - _bounds.X) / _dpiScale;
        double y = (screenY - _bounds.Y) / _dpiScale;

        var brush = (Brush)(TryFindResource("AccentBrush") ?? Brushes.DeepSkyBlue);
        const double size = 46;
        var ring = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = brush,
            StrokeThickness = 3,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.25, 0.25),
        };
        Canvas.SetLeft(ring, x - size / 2);
        Canvas.SetTop(ring, y - size / 2);
        RippleCanvas.Children.Add(ring);

        var dur = TimeSpan.FromMilliseconds(480);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.25, 1.0, dur) { EasingFunction = ease };
        var fade = new DoubleAnimation(0.85, 0.0, dur) { EasingFunction = ease };
        fade.Completed += (_, _) => RippleCanvas.Children.Remove(ring);

        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, fade);
    }
}
