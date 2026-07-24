using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using RecMode.Capture;

namespace RecMode.App.Views;

/// <summary>
/// Fullscreen keystroke-visualizer overlay: shows the most recent hotkey combo (e.g. "Ctrl + Z") as a pill
/// near the bottom of the primary monitor, fading in/out. Click-through (WS_EX_TRANSPARENT) so it never
/// intercepts input, non-activating, and deliberately NOT excluded from capture — the whole point is that the
/// combo is visible in the recording, mirroring <see cref="ClickRippleOverlay"/>'s approach.
/// </summary>
public partial class KeystrokeOverlayWindow : Window
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

    public KeystrokeOverlayWindow()
    {
        InitializeComponent();
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        _monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLongW(hwnd, GWL_EXSTYLE);
        _ = SetWindowLongW(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        SetWindowPos(hwnd, IntPtr.Zero, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Shows (or replaces) the current combo, restarting the pop-in/hold/fade-out cycle.</summary>
    public void ShowCombo(string combo)
    {
        ComboText.Text = combo;

        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1350)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

        var scale = new DoubleAnimationUsingKeyFrames();
        scale.KeyFrames.Add(new EasingDoubleKeyFrame(0.92, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        Pill.BeginAnimation(OpacityProperty, opacity);
        PillScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        PillScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
    }
}
