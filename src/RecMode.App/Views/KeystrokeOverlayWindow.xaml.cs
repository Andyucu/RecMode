using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using RecMode.Capture;

namespace RecMode.App.Views;

/// <summary>
/// Fullscreen keystroke-visualizer overlay: shows the most recent hotkey combo (e.g. "Ctrl + Z") as a pill
/// near the bottom of whatever is actually being recorded, fading in/out. Click-through (WS_EX_TRANSPARENT) so
/// it never intercepts input, non-activating, and deliberately NOT excluded from capture — the whole point is
/// that the combo is visible in the recording, mirroring <see cref="ClickRippleOverlay"/>'s approach (and now
/// its target-resolution too — this used to always cover the primary monitor regardless of which one was
/// actually being recorded, so it silently rendered on the wrong display for any non-primary source).
/// </summary>
public partial class KeystrokeOverlayWindow : Window
{
    private readonly RegionRect _bounds;

    public KeystrokeOverlayWindow(CaptureTarget? target)
    {
        InitializeComponent();
        if (target is not null && CaptureCapabilities.TryGetScreenBounds(target, out RegionRect bounds))
        {
            _bounds = bounds;
        }
        else
        {
            IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
            MonitorInfo mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            _bounds = new RegionRect(mon.X, mon.Y, mon.Width, mon.Height);
        }

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        OverlayWindowStyle.ApplyClickThrough(hwnd);
        OverlayWindowStyle.SetBounds(hwnd, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height);
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
