using System.Runtime.InteropServices;
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
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

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
        SetWindowPos(hwnd, IntPtr.Zero, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);
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
