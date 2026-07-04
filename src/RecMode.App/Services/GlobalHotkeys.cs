using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RecMode.App.Services;

/// <summary>
/// Registers global hotkeys on a message-only window (plan §3.2). Construct on the UI thread (needs a
/// message pump). Registration failures are reported via <see cref="RegistrationFailed"/> so the caller can
/// surface a RecoverableWarning (e.g. the key is already taken by another app).
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly List<int> _registered = [];
    private int _nextId = 1;

    public GlobalHotkeys()
    {
        _source = new HwndSource(new HwndSourceParameters("RecModeHotkeys")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>Raised (on the UI thread) when a registered hotkey fires, with the id returned by <see cref="Register"/>.</summary>
    public event Action<int>? Pressed;

    /// <summary>Raised when a registration fails (key already in use).</summary>
    public event Action<uint>? RegistrationFailed;

    /// <summary>Registers a hotkey; returns its id (or -1 on failure).</summary>
    public int Register(uint modifiers, uint virtualKey)
    {
        int id = _nextId++;
        if (RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
        {
            _registered.Add(id);
            return id;
        }

        RegistrationFailed?.Invoke(virtualKey);
        return -1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            Pressed?.Invoke(wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _registered)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

/// <summary>Common virtual-key codes for the default hotkeys.</summary>
public static class VirtualKeys
{
    public const uint F9 = 0x78;
    public const uint F10 = 0x79;
    public const uint F11 = 0x7A;
}
