using System.Runtime.InteropServices;

namespace RecMode.App.Services;

/// <summary>
/// Low-level global mouse hook (<c>WH_MOUSE_LL</c>) that reports left/right button-down screen positions —
/// the input for the click-highlight ripple (plan Phase 8). See <see cref="LowLevelHook"/> for the shared
/// hook plumbing.
/// </summary>
public sealed class GlobalMouseHook() : LowLevelHook(WH_MOUSE_LL)
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct { public Point Pt; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    /// <summary>Raised (on the UI thread) with the screen coordinates of a left/right button-down.</summary>
    public event Action<int, int>? Clicked;

    protected override void HandleMessage(IntPtr wParam, IntPtr lParam)
    {
        if (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            Clicked?.Invoke(data.Pt.X, data.Pt.Y);
        }
    }
}
