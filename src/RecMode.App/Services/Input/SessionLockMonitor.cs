using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RecMode.App.Services;

/// <summary>
/// Reports Windows session lock/unlock (<c>WM_WTSSESSION_CHANGE</c>) on a message-only window — the signal
/// behind the auto-pause safety guard (a locked workstation has nothing useful on screen to record, and
/// keeping capture/encoding running while stepped away burns CPU and disk for no benefit, per §3.9). Mirrors
/// <see cref="GlobalHotkeys"/>'s message-only-window shape and <see cref="GlobalMouseHook"/>'s
/// Install/Uninstall lifecycle — the window is created once (cheap, app-lifetime), but the actual session
/// notification subscription is only active between <see cref="Install"/>/<see cref="Uninstall"/>, so it only
/// runs while something (the auto-pause guard) actually needs it, i.e. while recording.
/// </summary>
public sealed class SessionLockMonitor : IDisposable
{
    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int WTS_SESSION_LOCK = 0x7;
    private const int NOTIFY_FOR_THIS_SESSION = 0;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    private readonly HwndSource _source;
    private bool _installed;

    public SessionLockMonitor()
    {
        _source = new HwndSource(new HwndSourceParameters("RecModeSessionLock")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>Raised (on the UI thread) when the workstation is locked.</summary>
    public event Action? Locked;

    public void Install()
    {
        if (!_installed)
        {
            _installed = WTSRegisterSessionNotification(_source.Handle, NOTIFY_FOR_THIS_SESSION);
        }
    }

    public void Uninstall()
    {
        if (_installed)
        {
            WTSUnRegisterSessionNotification(_source.Handle);
            _installed = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WTSSESSION_CHANGE && wParam.ToInt32() == WTS_SESSION_LOCK)
        {
            Locked?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Uninstall();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
