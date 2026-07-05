using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>
/// Keeps RecMode's own overlay windows (countdown, recording toolbar) out of the capture via
/// <c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c> — Win10 2004+ / Win11 (plan §3.6). On older
/// builds the call is skipped (the overlay would appear in the recording, which is the documented Win10 gap).
/// </summary>
public static class CaptureExclusion
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    /// <summary>Excludes <paramref name="window"/> from screen capture when the OS supports it. Call after the HWND exists (SourceInitialized).</summary>
    public static void Apply(Window window, IOsCapabilities os)
    {
        if (!os.SupportsExcludeFromCapture)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }
    }
}
