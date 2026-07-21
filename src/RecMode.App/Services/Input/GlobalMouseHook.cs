using System.Runtime.InteropServices;

namespace RecMode.App.Services;

/// <summary>
/// Low-level global mouse hook (<c>WH_MOUSE_LL</c>) that reports left/right button-down screen positions —
/// the input for the click-highlight ripple (plan Phase 8). Install on the UI thread (needs its message loop);
/// the callback must stay fast, so it only marshals the point and passes the event on.
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct { public Point Pt; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    private readonly HookProc _proc; // kept alive for the hook's lifetime
    private IntPtr _hook;

    public GlobalMouseHook() => _proc = Callback;

    /// <summary>Raised (on the UI thread) with the screen coordinates of a left/right button-down.</summary>
    public event Action<int, int>? Clicked;

    public void Install()
    {
        if (_hook == IntPtr.Zero)
        {
            _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
        }
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN))
        {
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            Clicked?.Invoke(data.Pt.X, data.Pt.Y);
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
