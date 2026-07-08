using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RecMode.App.Themes;

/// <summary>
/// Applies the Win11 Mica backdrop and immersive dark title bar via DWM (plan §2). No-ops gracefully on
/// Win10, where the caller falls back to an opaque themed background.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowBackdrop
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Sets the title-bar dark/light mode. Safe on Win10 19041+.</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int flag = dark ? 1 : 0;
        try { _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref flag, sizeof(int)); }
        catch (DllNotFoundException) { }
    }

    /// <summary>Requests the Mica backdrop (Win11 only). Returns true if the call succeeded.</summary>
    public static bool TryEnableMica(IntPtr hwnd)
    {
        int backdrop = DWMSBT_MAINWINDOW;
        try
        {
            return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
