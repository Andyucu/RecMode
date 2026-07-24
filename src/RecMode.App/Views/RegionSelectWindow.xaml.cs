using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Views;

/// <summary>
/// Full-monitor overlay for region selection (plan Phase 2): dim the screen, rubber-band a rectangle with a
/// live px readout, offer presets, and return the region in monitor-local pixels. DPI-safe: the window is
/// positioned in physical pixels and selection DIPs are converted with the composition transform.
/// </summary>
public partial class RegionSelectWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly MonitorInfo _monitor;
    private readonly IOsCapabilities? _excludeFromCaptureOs;
    private Point _start;
    private bool _dragging;
    private double _dpiScale = 1.0;

    public RegionRect? Result { get; private set; }

    /// <param name="excludeFromCaptureOs">Non-null to hide this picker overlay itself from an in-progress
    /// recording (manual zoom) via <see cref="CaptureExclusion"/>; null (the default caller) for the ordinary
    /// pre-recording Region-source picker, which has nothing running yet to worry about capturing into.</param>
    public RegionSelectWindow(MonitorInfo monitor, IOsCapabilities? excludeFromCaptureOs = null)
    {
        InitializeComponent();
        _monitor = monitor;
        _excludeFromCaptureOs = excludeFromCaptureOs;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Cover the target monitor in physical pixels (bypasses DIP/DPI conversion for positioning).
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, IntPtr.Zero, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);

        if (_excludeFromCaptureOs is not null)
        {
            CaptureExclusion.Apply(this, _excludeFromCaptureOs);
        }
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

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(SelectionCanvas);
        _dragging = true;
        UpdateRect(_start, _start);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            UpdateRect(_start, e.GetPosition(SelectionCanvas));
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => _dragging = false;

    private void UpdateRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(a.X - b.X), h = Math.Abs(a.Y - b.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
        SelectionRect.Visibility = Visibility.Visible;

        int pxW = (int)Math.Round(w * _dpiScale);
        int pxH = (int)Math.Round(h * _dpiScale);
        SizeText.Text = $"{pxW} × {pxH}";
        Canvas.SetLeft(SizeLabel, x);
        Canvas.SetTop(SizeLabel, Math.Max(0, y - 28));
        SizeLabel.Visibility = Visibility.Visible;
        ConfirmButton.IsEnabled = pxW >= 16 && pxH >= 16;
    }

    private void SetPresetPixels(int pxW, int pxH)
    {
        pxW = Math.Min(pxW, _monitor.Width);
        pxH = Math.Min(pxH, _monitor.Height);
        double w = pxW / _dpiScale, h = pxH / _dpiScale;
        double cx = SelectionCanvas.ActualWidth / 2, cy = SelectionCanvas.ActualHeight / 2;
        UpdateRect(new Point(cx - w / 2, cy - h / 2), new Point(cx + w / 2, cy + h / 2));
    }

    private void OnPreset1080(object sender, RoutedEventArgs e) => SetPresetPixels(1920, 1080);
    private void OnPreset720(object sender, RoutedEventArgs e) => SetPresetPixels(1280, 720);
    private void OnPresetFull(object sender, RoutedEventArgs e) => SetPresetPixels(_monitor.Width, _monitor.Height);

    private void OnConfirm(object sender, RoutedEventArgs e) => Confirm();
    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
        {
            Confirm();
        }
    }

    private void Confirm()
    {
        double left = Canvas.GetLeft(SelectionRect);
        double top = Canvas.GetTop(SelectionRect);
        int x = Math.Clamp((int)Math.Round(left * _dpiScale), 0, _monitor.Width - 2);
        int y = Math.Clamp((int)Math.Round(top * _dpiScale), 0, _monitor.Height - 2);
        int w = MakeEven(Math.Clamp((int)Math.Round(SelectionRect.Width * _dpiScale), 2, _monitor.Width - x));
        int h = MakeEven(Math.Clamp((int)Math.Round(SelectionRect.Height * _dpiScale), 2, _monitor.Height - y));

        Result = new RegionRect(x, y, w, h);
        DialogResult = true;
        Close();
    }

    private static int MakeEven(int v) => v % 2 == 0 ? v : v - 1;
}
