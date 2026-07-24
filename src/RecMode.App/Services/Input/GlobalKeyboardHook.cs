using System.Runtime.InteropServices;

namespace RecMode.App.Services;

/// <summary>
/// Low-level global keyboard hook (<c>WH_KEYBOARD_LL</c>) that reports non-autorepeat key-down events plus
/// the modifier keys held at that instant — the input for the on-screen keystroke visualizer. Install on the
/// UI thread (needs its message loop); the callback must stay fast, so it only tracks down/up state and
/// marshals the event on. Autorepeat (a key held down) is filtered here via a down-set, since the low-level
/// hook struct carries no "previous state" bit to detect repeats itself.
/// </summary>
public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    private static readonly uint[] ShiftCodes = [0x10, 0xA0, 0xA1];
    private static readonly uint[] CtrlCodes = [0x11, 0xA2, 0xA3];
    private static readonly uint[] AltCodes = [0x12, 0xA4, 0xA5];
    private static readonly uint[] WinCodes = [0x5B, 0x5C];

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct { public uint VkCode; public uint ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

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
    private readonly HashSet<uint> _down = [];
    private IntPtr _hook;

    public GlobalKeyboardHook() => _proc = Callback;

    /// <summary>Raised (on the UI thread) on a fresh key-down (not autorepeat), with the vk code and the
    /// modifier keys held at that instant: (vkCode, ctrl, alt, shift, win).</summary>
    public event Action<uint, bool, bool, bool, bool>? KeyDown;

    public void Install()
    {
        if (_hook == IntPtr.Zero)
        {
            _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
        }
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _down.Clear();
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (_down.Add(data.VkCode))
                {
                    KeyDown?.Invoke(data.VkCode, _down.Overlaps(CtrlCodes), _down.Overlaps(AltCodes),
                        _down.Overlaps(ShiftCodes), _down.Overlaps(WinCodes));
                }
            }
            else if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                _down.Remove(data.VkCode);
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
