using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Views;

/// <summary>
/// A thin colored outline drawn exactly around a screen rect (a selected/recording Region or Window source),
/// so it's always obvious what's actually about to be — or is being — captured. Excluded from capture
/// (<see cref="CaptureExclusion"/>), like the countdown/toolbar overlays: this is a live on-screen indicator
/// for the person recording, not something meant to appear in the output.
/// <para>
/// In interactive mode (Region sources only — see <see cref="SourceContourService"/>, toggled via
/// <see cref="SetInteractive"/>) the outline becomes draggable — grab a corner's larger, subtly-shaded move
/// zone to reposition the whole rect, or its small white handle (nested inside that same corner) to resize —
/// live, including while a recording is in progress: <see cref="BoundsDragged"/> fires continuously during the
/// drag so the caller can retarget the actual capture in real time, not just move a visual indicator. Only the
/// 4 corners accept input at all (see <see cref="WndProc"/>); edges and the interior are click-through, so
/// dragging never blocks interacting with whatever's actually inside the recorded area. Window sources stay
/// fully click-through/passive: their bounds come from the OS window itself, not a free rect to drag around.
/// </para>
/// </summary>
public partial class ContourOverlayWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLongW(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const int MinSize = 40;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;

    /// <summary>Extra room reserved around the logical contour rect so the corner resize handles (centered on
    /// the true corners) aren't clipped at the window edge — matches the XAML's Border/handle margins.</summary>
    private const int HandlePadding = 10;

    /// <summary>Side length of the square hit zone in each corner (window-relative, from that corner) that
    /// actually accepts mouse input in interactive mode — matches the move grips' XAML size. Everywhere else
    /// (edges and the whole interior) is click-through, via <see cref="WndProc"/>, so dragging never blocks
    /// interacting with whatever's actually being recorded.</summary>
    private const int CornerZone = 32;

    private static readonly SolidColorBrush SelectedBrush = new(Colors.Gold);
    private static readonly SolidColorBrush RecordingBrush = new(Color.FromRgb(0xE8, 0x1B, 0x3A));

    private readonly IOsCapabilities _os;
    private bool _interactive;
    private RegionRect _monitorBounds = new(0, 0, int.MaxValue, int.MaxValue);
    private RegionRect _current;

    // Drag/resize state. Corner is null while moving the whole rect (body drag).
    private bool _dragging;
    private string? _resizeCorner;
    private POINT _dragStartCursor;
    private RegionRect _dragStartBounds;

    public ContourOverlayWindow(IOsCapabilities os)
    {
        InitializeComponent();
        _os = os;
        SourceInitialized += OnSourceInitialized;
        ContourBorder.BorderBrush = SelectedBrush;

        RootGrid.MouseLeftButtonDown += OnMouseLeftButtonDown;
        RootGrid.MouseMove += OnMouseMove;
        RootGrid.MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    /// <summary>Raised (screen-space, physical pixels) continuously while the outline is being dragged/resized.</summary>
    public event Action<RegionRect>? BoundsDragged;

    /// <summary>Raised once when a drag/resize gesture ends (mouse released) — the point to persist the result.</summary>
    public event Action? DragCompleted;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyHitTestStyle();
        CaptureExclusion.Apply(this, _os);
        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
    }

    /// <summary>
    /// Restricts mouse input to the 4 <see cref="CornerZone"/> squares (in interactive mode only) by answering
    /// <c>WM_NCHITTEST</c> with <c>HTTRANSPARENT</c> everywhere else, which makes Windows pass the click on to
    /// whatever real window is underneath instead of this overlay swallowing it. Removing
    /// <c>WS_EX_TRANSPARENT</c> (see <see cref="ApplyHitTestStyle"/>) makes the *whole* window receive input;
    /// this is what narrows that back down to just the corners once it does.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST || !_interactive)
        {
            return IntPtr.Zero;
        }

        int screenX = unchecked((short)(lParam.ToInt32() & 0xFFFF));
        int screenY = unchecked((short)((lParam.ToInt32() >> 16) & 0xFFFF));

        // Window-relative coordinates: the window spans _current (the logical rect) inflated by HandlePadding.
        int localX = screenX - (_current.X - HandlePadding);
        int localY = screenY - (_current.Y - HandlePadding);
        int windowW = _current.Width + HandlePadding * 2;
        int windowH = _current.Height + HandlePadding * 2;

        handled = true;
        return (IntPtr)(IsInCornerZone(localX, localY, windowW, windowH, CornerZone) ? HTCLIENT : HTTRANSPARENT);
    }

    /// <summary>True if a window-relative point falls within any of the 4 <paramref name="cornerZone"/>-sized
    /// squares at the corners of a <paramref name="windowW"/>×<paramref name="windowH"/> window — pulled out
    /// as a pure function so it's directly unit-testable, same rationale as <see cref="ClampResize"/>.</summary>
    internal static bool IsInCornerZone(int localX, int localY, int windowW, int windowH, int cornerZone) =>
        (localX < cornerZone && localY < cornerZone) ||
        (localX >= windowW - cornerZone && localY < cornerZone) ||
        (localX < cornerZone && localY >= windowH - cornerZone) ||
        (localX >= windowW - cornerZone && localY >= windowH - cornerZone);

    /// <summary>Enables/disables drag-to-move + corner-resize. Window sources always pass false (their bounds
    /// follow the OS window, not a free rect); Region sources pass true regardless of recording state.</summary>
    public void SetInteractive(bool interactive)
    {
        if (_interactive == interactive)
        {
            return;
        }

        _interactive = interactive;
        ApplyHitTestStyle();
        Visibility handleVisibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        HandleTopLeft.Visibility = handleVisibility;
        HandleTopRight.Visibility = handleVisibility;
        HandleBottomLeft.Visibility = handleVisibility;
        HandleBottomRight.Visibility = handleVisibility;
        MoveGripTopLeft.Visibility = handleVisibility;
        MoveGripTopRight.Visibility = handleVisibility;
        MoveGripBottomLeft.Visibility = handleVisibility;
        MoveGripBottomRight.Visibility = handleVisibility;
    }

    /// <summary>The monitor's absolute screen bounds — dragging/resizing clamps to stay fully within them.</summary>
    public void SetMonitorBounds(RegionRect monitorBounds) => _monitorBounds = monitorBounds;

    private void ApplyHitTestStyle()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int ex = GetWindowLongW(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        ex = _interactive ? ex & ~WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT;
        _ = SetWindowLongW(hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>Moves/resizes the outline to exactly cover <paramref name="bounds"/> (absolute virtual-desktop
    /// physical pixels).</summary>
    public void UpdateBounds(RegionRect bounds)
    {
        _current = bounds;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero, bounds.X - HandlePadding, bounds.Y - HandlePadding,
                bounds.Width + HandlePadding * 2, bounds.Height + HandlePadding * 2, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>Yellow while just selected (not yet recording), red while actively recording.</summary>
    public void SetRecording(bool recording) => ContourBorder.BorderBrush = recording ? RecordingBrush : SelectedBrush;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_interactive)
        {
            return;
        }

        _resizeCorner = (e.OriginalSource as Rectangle)?.Name switch
        {
            nameof(HandleTopLeft) => "TL",
            nameof(HandleTopRight) => "TR",
            nameof(HandleBottomLeft) => "BL",
            nameof(HandleBottomRight) => "BR",
            _ => null,
        };

        GetCursorPos(out _dragStartCursor);
        _dragStartBounds = _current;
        _dragging = true;
        RootGrid.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        GetCursorPos(out POINT cursor);
        int dx = cursor.X - _dragStartCursor.X;
        int dy = cursor.Y - _dragStartCursor.Y;

        RegionRect next = _resizeCorner switch
        {
            "TL" => ClampResize(new RegionRect(_dragStartBounds.X + dx, _dragStartBounds.Y + dy,
                _dragStartBounds.Width - dx, _dragStartBounds.Height - dy), _monitorBounds, MinSize),
            "TR" => ClampResize(new RegionRect(_dragStartBounds.X, _dragStartBounds.Y + dy,
                _dragStartBounds.Width + dx, _dragStartBounds.Height - dy), _monitorBounds, MinSize),
            "BL" => ClampResize(new RegionRect(_dragStartBounds.X + dx, _dragStartBounds.Y,
                _dragStartBounds.Width - dx, _dragStartBounds.Height + dy), _monitorBounds, MinSize),
            "BR" => ClampResize(new RegionRect(_dragStartBounds.X, _dragStartBounds.Y,
                _dragStartBounds.Width + dx, _dragStartBounds.Height + dy), _monitorBounds, MinSize),
            // Same-size move: AutoZoomMath.Clamp already does exactly "shift fully inside bounds, only
            // shrinking if the rect is actually bigger than bounds" — precisely what a move (never resizing)
            // needs, so this reuses it rather than duplicating the same logic under a different name.
            _ => AutoZoomMath.Clamp(new RegionRect(_dragStartBounds.X + dx, _dragStartBounds.Y + dy,
                _dragStartBounds.Width, _dragStartBounds.Height), _monitorBounds),
        };

        UpdateBounds(next);
        BoundsDragged?.Invoke(next);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _resizeCorner = null;
        RootGrid.ReleaseMouseCapture();
        DragCompleted?.Invoke();
    }

    /// <summary>Keeps a corner-resized rect's edges inside <paramref name="bounds"/> and its size at/above
    /// <paramref name="minSize"/> — pulled out as a pure function (rather than reading instance state) so it's
    /// directly unit-testable, mirroring <c>AutoZoomMath</c>'s pattern for this exact kind of geometry.</summary>
    internal static RegionRect ClampResize(RegionRect rect, RegionRect bounds, int minSize)
    {
        int right = rect.X + rect.Width;
        int bottom = rect.Y + rect.Height;
        int x = Math.Clamp(rect.X, bounds.X, right - minSize);
        int y = Math.Clamp(rect.Y, bounds.Y, bottom - minSize);
        right = Math.Min(right, bounds.X + bounds.Width);
        bottom = Math.Min(bottom, bounds.Y + bounds.Height);
        int w = Math.Max(minSize, right - x);
        int h = Math.Max(minSize, bottom - y);
        return new RegionRect(x, y, w, h);
    }
}
