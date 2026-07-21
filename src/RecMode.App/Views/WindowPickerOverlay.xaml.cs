using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using RecMode.Capture;

namespace RecMode.App.Views;

/// <summary>
/// Full-virtual-desktop overlay for "pick a window with the mouse" (alternative to the Window-source combo
/// box): dims the whole desktop, highlights whichever top-level window is currently under the cursor as the
/// mouse moves, and returns it on click. Spans every monitor (not just one) since the target window could be
/// on any of them. DPI-safe the same way <see cref="RegionSelectWindow"/> is: positioned in physical pixels,
/// hit-testing done entirely in physical pixels via <see cref="CaptureCapabilities.FindWindowAtPoint"/>, and
/// only the on-screen highlight rectangle needs converting back to this window's own DIP space.
/// </summary>
public partial class WindowPickerOverlay : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private int _originX;
    private int _originY;
    private double _dpiScale = 1.0;
    private WindowInfo? _hovered;

    public WindowInfo? Result { get; private set; }

    public WindowPickerOverlay()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnMouseDown;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        _originX = monitors.Min(m => m.X);
        _originY = monitors.Min(m => m.Y);
        int width = monitors.Max(m => m.X + m.Width) - _originX;
        int height = monitors.Max(m => m.Y + m.Height) - _originY;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, IntPtr.Zero, _originX, _originY, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        System.Windows.Media.CompositionTarget? ct = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (ct is not null)
        {
            _dpiScale = ct.TransformToDevice.M11;
        }

        Activate();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point dip = e.GetPosition(this);
        int physX = _originX + (int)Math.Round(dip.X * _dpiScale);
        int physY = _originY + (int)Math.Round(dip.Y * _dpiScale);

        WindowInfo? hit = CaptureCapabilities.FindWindowAtPoint(physX, physY);
        if (hit?.Handle == _hovered?.Handle)
        {
            return;
        }

        _hovered = hit;
        if (hit is not null && CaptureCapabilities.TryGetWindowScreenRect(hit.Handle, out RegionRect r))
        {
            double left = (r.X - _originX) / _dpiScale;
            double top = (r.Y - _originY) / _dpiScale;
            double w = r.Width / _dpiScale;
            double h = r.Height / _dpiScale;

            Canvas.SetLeft(HighlightRect, left);
            Canvas.SetTop(HighlightRect, top);
            HighlightRect.Width = w;
            HighlightRect.Height = h;
            HighlightRect.Visibility = Visibility.Visible;

            TitleText.Text = hit.Title;
            Canvas.SetLeft(TitleLabel, left);
            Canvas.SetTop(TitleLabel, Math.Max(0, top - 28));
            TitleLabel.Visibility = Visibility.Visible;
        }
        else
        {
            HighlightRect.Visibility = Visibility.Collapsed;
            TitleLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_hovered is null)
        {
            return;
        }

        Result = _hovered;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
