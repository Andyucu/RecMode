using System.Runtime.InteropServices;

namespace RecMode.App.Services;

/// <summary>
/// Shared plumbing for a low-level Win32 global input hook (<c>WH_MOUSE_LL</c>/<c>WH_KEYBOARD_LL</c>) —
/// <see cref="GlobalMouseHook"/> and <see cref="GlobalKeyboardHook"/> were previously two independent,
/// near-identical implementations of the same <c>SetWindowsHookExW</c>/<c>UnhookWindowsHookEx</c>/
/// <c>CallNextHookEx</c> P/Invoke set and hook-handle lifetime; only which hook id and what a subclass does
/// with the raw <c>wParam</c>/<c>lParam</c> actually differ. Install on the UI thread (needs its message
/// loop); <see cref="HandleMessage"/> must stay fast, same as any low-level hook callback.
/// </summary>
public abstract class LowLevelHook : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private readonly int _idHook;
    private readonly HookProc _proc; // kept alive for the hook's lifetime
    private IntPtr _hook;

    protected LowLevelHook(int idHook)
    {
        _idHook = idHook;
        _proc = Callback;
    }

    public void Install()
    {
        if (_hook == IntPtr.Zero)
        {
            _hook = SetWindowsHookExW(_idHook, _proc, GetModuleHandleW(null), 0);
        }
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        OnUninstalled();
    }

    /// <summary>Called after the hook handle is cleared, so a subclass can reset its own per-session state
    /// (e.g. <see cref="GlobalKeyboardHook"/>'s held-key set) without each duplicating <see cref="Uninstall"/>.</summary>
    protected virtual void OnUninstalled() { }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            HandleMessage(wParam, lParam);
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Interpret one hook message. Only called when <c>code &gt;= 0</c> (the Win32 low-level-hook
    /// convention: negative codes must be passed straight through, unexamined, to <c>CallNextHookEx</c>).</summary>
    protected abstract void HandleMessage(IntPtr wParam, IntPtr lParam);

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
