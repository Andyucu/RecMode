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
/// in the accent colour, sized to whatever is actually being recorded (a single monitor/region/window, or the
/// full virtual desktop for All Displays — never the other monitors when recording just one). Deliberately NOT
/// excluded from capture so the ink is part of the recording. Esc exits (via the callback), right-click clears.
/// </summary>
public partial class AnnotationOverlay : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const int RGN_DIFF = 4;

    private readonly RegionRect _bounds;
    private readonly Action _onExit;

    public AnnotationOverlay(Action onExit, CaptureTarget? target)
    {
        InitializeComponent();
        _onExit = onExit;
        if (target is not null && CaptureCapabilities.TryGetScreenBounds(target, out RegionRect bounds))
        {
            _bounds = bounds;
        }
        else
        {
            // Nothing being recorded (or its bounds couldn't be resolved) — fall back to the primary monitor.
            IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
            MonitorInfo mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            _bounds = new RegionRect(mon.X, mon.Y, mon.Width, mon.Height);
        }

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
        SetWindowPos(hwnd, IntPtr.Zero, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Carves the given absolute-screen-pixel rectangles (the exit-hint bar, the recording toolbar) out of
    /// this window's hit-test/paint region, so ink can never be drawn over them and clicks intended for their
    /// buttons always reach them instead of being swallowed by the full-screen ink surface sitting on top.
    /// </summary>
    public void ExcludeRects(IReadOnlyList<RegionRect> screenRects)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr full = CreateRectRgn(0, 0, _bounds.Width, _bounds.Height);
        foreach (RegionRect r in screenRects)
        {
            int x1 = r.X - _bounds.X;
            int y1 = r.Y - _bounds.Y;
            IntPtr hole = CreateRectRgn(x1, y1, x1 + r.Width, y1 + r.Height);
            _ = CombineRgn(full, full, hole, RGN_DIFF);
            DeleteObject(hole);
        }

        // SetWindowRgn takes ownership of `full` on success; only free it if the call failed.
        if (SetWindowRgn(hwnd, full, true) == 0)
        {
            DeleteObject(full);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _onExit();
        }
    }
}
