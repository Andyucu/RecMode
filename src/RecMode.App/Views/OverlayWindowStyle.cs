using System.Runtime.InteropServices;

namespace RecMode.App.Views;

/// <summary>
/// Shared Win32 plumbing for this app's small always-on-top overlay windows (click ripple, keystroke pill,
/// contour outline, countdown, region/window pickers, screenshot flash, annotation canvas) — every one of
/// them independently declared the same <c>SetWindowPos</c>/<c>GetWindowLongW</c>/<c>SetWindowLongW</c>
/// P/Invokes and the same extended-style constants. <see cref="SetBounds"/> covers the common "position this
/// window at an absolute virtual-desktop rect" call every overlay makes; <see cref="ApplyClickThrough"/>
/// covers the two overlays (click ripple, keystroke pill) that are purely passive indicators. Overlays with
/// bespoke ex-style logic (e.g. <see cref="ContourOverlayWindow"/>'s interactive/click-through toggle) still
/// compose their own from <see cref="GetExStyle"/>/<see cref="SetExStyle"/> and the exposed constants, rather
/// than duplicating the raw P/Invoke declarations.
/// </summary>
internal static class OverlayWindowStyle
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLongW(IntPtr h, int i, int v);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x80;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    /// <summary>Positions/sizes a window at an absolute virtual-desktop rect without stealing z-order or
    /// activation — every overlay window's positioning call, regardless of what it uses the rect for.</summary>
    public static void SetBounds(IntPtr hwnd, int x, int y, int width, int height) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

    public static int GetExStyle(IntPtr hwnd) => GetWindowLongW(hwnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hwnd, int style) => _ = SetWindowLongW(hwnd, GWL_EXSTYLE, style);

    /// <summary>Layered, click-through, non-activating, no-taskbar-entry — a purely passive on-screen
    /// indicator that must never intercept input or steal focus (click ripple, keystroke pill).</summary>
    public static void ApplyClickThrough(IntPtr hwnd) =>
        SetExStyle(hwnd, GetExStyle(hwnd) | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
}
