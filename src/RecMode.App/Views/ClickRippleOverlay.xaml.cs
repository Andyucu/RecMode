using System.Runtime.InteropServices;
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
/// excluded from capture so the ripple is part of the recording. Covers the primary monitor.
/// </summary>
public partial class ClickRippleOverlay : Window
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLongW(IntPtr h, int i, int v);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    private readonly MonitorInfo _monitor;
    private double _dpiScale = 1.0;

    public ClickRippleOverlay()
    {
        InitializeComponent();
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        _monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLongW(hwnd, GWL_EXSTYLE);
        _ = SetWindowLongW(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        SetWindowPos(hwnd, IntPtr.Zero, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);
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
        // Only ripple clicks on the covered monitor.
        if (screenX < _monitor.X || screenX >= _monitor.X + _monitor.Width ||
            screenY < _monitor.Y || screenY >= _monitor.Y + _monitor.Height)
        {
            return;
        }

        double x = (screenX - _monitor.X) / _dpiScale;
        double y = (screenY - _monitor.Y) / _dpiScale;

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
