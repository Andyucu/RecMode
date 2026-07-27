using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Views;

/// <summary>
/// Brief full-monitor white flash after a screenshot (design: <c>rm-flash</c>, opacity 0.85→0 over 0.28s) —
/// the "shutter" feedback cue. Excluded from capture like the other transient app-chrome overlays (toolbar,
/// countdown); unlike click-ripple/annotation, this isn't user content, so it shouldn't appear in a recording.
/// </summary>
public partial class ScreenshotFlashWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly IOsCapabilities _os;

    public ScreenshotFlashWindow(MonitorInfo monitor, IOsCapabilities os)
    {
        InitializeComponent();
        _monitor = monitor;
        _os = os;
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => StartFlash();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        OverlayWindowStyle.SetBounds(hwnd, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height);
        CaptureExclusion.Apply(this, _os);
    }

    private void StartFlash()
    {
        var anim = new DoubleAnimation(0.85, 0.0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) => Close();
        FlashOverlay.BeginAnimation(OpacityProperty, anim);
    }
}
