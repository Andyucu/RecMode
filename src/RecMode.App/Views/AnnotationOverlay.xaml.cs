using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using RecMode.Capture;

namespace RecMode.App.Views;

/// <summary>
/// Fullscreen draw-on-screen annotation overlay (plan Phase 8): a freehand <see cref="System.Windows.Controls.InkCanvas"/>
/// in the accent colour, covering the primary monitor. Deliberately NOT excluded from capture so the ink is
/// part of the recording. Esc exits (via the callback), right-click clears. Covers the primary monitor.
/// </summary>
public partial class AnnotationOverlay : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);

    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    private readonly MonitorInfo _monitor;
    private readonly Action _onExit;

    public AnnotationOverlay(Action onExit)
    {
        InitializeComponent();
        _onExit = onExit;
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        _monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        Color accent = TryFindResource("AccentColor") is Color c ? c : Colors.DeepSkyBlue;
        Ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = accent,
            Width = 4,
            Height = 4,
            FitToCurve = true,
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { Activate(); Ink.Focus(); };
        KeyDown += OnKeyDown;
        MouseRightButtonDown += (_, _) => Ink.Strokes.Clear();
    }

    /// <summary>The ink surface — exposed so a self-test can add a stroke.</summary>
    public System.Windows.Controls.InkCanvas Canvas => Ink;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, IntPtr.Zero, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _onExit();
        }
    }
}
