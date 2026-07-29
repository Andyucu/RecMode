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

    private sealed class Registration
    {
        public int Id;
        public int RefCount;
    }

    private readonly HwndSource _source;
    private readonly List<int> _registered = [];
    // Reference-counted by (modifiers, virtualKey): Win32 only allows one RegisterHotKey per combo per HWND,
    // and every caller here shares the single message-only window, so two independent owners requesting the
    // identical combo (e.g. AnnotationService and ManualZoomService both wanting a bare Esc) used to have the
    // second Register() call fail outright — raising RegistrationFailed with a misleading "already in use by
    // another app" warning, even though the "other app" was this same process. Now the second (and any later)
    // caller gets back the same id as the first and both keep working; the native hotkey is only actually
    // unregistered once every owner has called Unregister.
    private readonly Dictionary<(uint Modifiers, uint VirtualKey), Registration> _byCombo = [];
    private readonly Dictionary<int, (uint Modifiers, uint VirtualKey)> _comboById = [];
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

    /// <summary>Raised at the end of <see cref="UnregisterAll"/>. <see cref="HotkeyBindings"/> is the only
    /// caller of <c>UnregisterAll</c>, but three other services (<c>SourceContourService</c>,
    /// <c>AnnotationService</c>, <c>ManualZoomService</c>) also register their own hotkeys (bare Esc/F12)
    /// through this same shared instance — a hotkey remap or the hotkey-capture UI's <c>Suspend()</c> wiped
    /// every registration process-wide, including theirs, with nothing telling them their cached ids had
    /// gone stale. Esc silently stopped clearing a region selection / exiting draw mode / exiting manual zoom
    /// after any rebind, with no error, until something else happened to force a fresh registration. Every
    /// owner of a hotkey registered here should subscribe and re-register (if still applicable) when this
    /// fires.</summary>
    public event Action? Cleared;

    /// <summary>Registers a hotkey; returns its id (or -1 on failure). If the same (modifiers, virtualKey)
    /// combo is already registered by another owner, returns that same id instead of attempting a duplicate
    /// native registration (which would fail) — see the field doc on <see cref="_byCombo"/>.</summary>
    public int Register(uint modifiers, uint virtualKey)
    {
        (uint modifiers, uint virtualKey) key = (modifiers, virtualKey);
        if (_byCombo.TryGetValue(key, out Registration? existing))
        {
            existing.RefCount++;
            return existing.Id;
        }

        int id = _nextId++;
        if (RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
        {
            _registered.Add(id);
            _byCombo[key] = new Registration { Id = id, RefCount = 1 };
            _comboById[id] = key;
            return id;
        }

        RegistrationFailed?.Invoke(virtualKey);
        return -1;
    }

    /// <summary>Probes whether a modifier+key combination could be registered right now, without touching any
    /// currently-registered hotkey and without raising <see cref="RegistrationFailed"/> — a probe "failing" is
    /// an expected, silent outcome the caller decides how to handle itself (e.g. hotkey-capture UI validation),
    /// not a real registration attempt gone wrong.</summary>
    public bool CanRegister(uint modifiers, uint virtualKey)
    {
        int id = _nextId++;
        bool ok = RegisterHotKey(_source.Handle, id, modifiers, virtualKey);
        if (ok)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        return ok;
    }

    /// <summary>Unregisters every hotkey (so the caller can rebind). Does not tear down the message window.</summary>
    public void UnregisterAll()
    {
        foreach (int id in _registered)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
        _byCombo.Clear();
        _comboById.Clear();
        Cleared?.Invoke();
    }

    /// <summary>Releases this caller's hold on the hotkey registered under <paramref name="id"/>. If another
    /// owner also holds the same (modifiers, virtualKey) combo (see <see cref="Register"/>), the native
    /// registration stays in place for them — it's only actually unregistered once every owner has released it.</summary>
    public void Unregister(int id)
    {
        if (!_comboById.TryGetValue(id, out (uint Modifiers, uint VirtualKey) key) ||
            !_byCombo.TryGetValue(key, out Registration? reg))
        {
            return;
        }

        if (--reg.RefCount > 0)
        {
            return;
        }

        _byCombo.Remove(key);
        _comboById.Remove(id);
        if (_registered.Remove(id))
        {
            UnregisterHotKey(_source.Handle, id);
        }
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

/// <summary>Virtual-key codes actually referenced by RecMode's own code — the F8-F11 default hotkeys are
/// configured through <see cref="HotkeyBindings"/>'s string-based parsing ("F9", "F10", ...) instead, so
/// those four constants used to exist here unreferenced by anything.</summary>
public static class VirtualKeys
{
    public const uint Escape = 0x1B;
    public const uint F12 = 0x7B;
}
