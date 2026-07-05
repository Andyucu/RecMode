using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Views;

/// <summary>
/// Full-monitor pre-roll countdown (plan Phase 5): dims the target display, ticks N→1, then closes with
/// <see cref="DialogResult"/> true so recording proceeds. Esc closes with false (cancelled). Excluded from
/// capture so it never appears in the recording.
/// </summary>
public partial class CountdownWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly MonitorInfo _monitor;
    private readonly IOsCapabilities _os;
    private readonly bool _excludeFromCapture;
    private readonly DispatcherTimer _timer;
    private int _remaining;

    public CountdownWindow(MonitorInfo monitor, int seconds, IOsCapabilities os, bool excludeFromCapture = true)
    {
        InitializeComponent();
        _monitor = monitor;
        _os = os;
        _excludeFromCapture = excludeFromCapture;
        _remaining = Math.Max(1, seconds);
        Number.Text = _remaining.ToString();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, IntPtr.Zero, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        if (_excludeFromCapture)
        {
            CaptureExclusion.Apply(this, _os);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Pop();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            _timer.Stop();
            DialogResult = true; // completed → proceed to record
            Close();
            return;
        }

        Number.Text = _remaining.ToString();
        Pop();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _timer.Stop();
            DialogResult = false; // cancelled
            Close();
        }
    }

    /// <summary>A brief scale+fade pop on each tick for a bit of life.</summary>
    private void Pop()
    {
        var scale = new DoubleAnimation(1.18, 1.0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);

        var fade = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(320));
        Bubble.BeginAnimation(OpacityProperty, fade);
    }
}
