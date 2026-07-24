using System.ComponentModel;
using System.Runtime.InteropServices;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Capture;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>
/// Draws a live outline (<see cref="ContourOverlayWindow"/>) around whatever Region or Window source is
/// currently selected on the Record screen — yellow while just selected, red while actually recording — so
/// it's always obvious what's about to be, or is being, captured. Screen/All-Displays sources aren't
/// outlined (the whole screen is already the obvious answer to "what's being recorded").
/// <para>
/// Region sources are also directly draggable/resizable (a corner's larger grip to move, its small handle to
/// resize), live, including while a recording is in progress — dragging retargets the actual capture, not
/// just a visual indicator (<see cref="RecordingCoordinator.SetBaseRect"/> while recording,
/// <see cref="RecordViewModel.UpdateRegionFromDrag"/> beforehand). Window sources stay click-through/passive:
/// their bounds come from the OS window itself, not a free rect a user would drag around. A global Esc hotkey
/// clears the Region selection entirely (reverts to Screen) while it's shown in its "selected, not recording"
/// state.
/// </para>
/// <para>
/// Visible whenever the Record page is the active, non-minimized page (mirrors the live-preview lifecycle,
/// §3.9), <em>or</em> a recording is in progress regardless of which page is showing — matching how the
/// floating toolbar and title-bar status pill already stay up throughout a recording.
/// </para>
/// A Window source's on-screen rect can move/resize without any RecordViewModel property changing (the user
/// dragging the target window around), so this is the one place in the class that has to track it separately —
/// done via a genuine <c>WinEventHook</c> (<c>EVENT_OBJECT_LOCATIONCHANGE</c>) on that one window's owning
/// process rather than a poll, so the contour tracks a window drag in real time with no added latency and no
/// idle cost (event-driven per §3.9, not the polling trade-off used elsewhere for window-resize detection in
/// <c>RecordingCoordinator.PaceLoop</c>, which reads a WGC frame's actual size rather than an OS notification).
/// </summary>
public sealed class SourceContourService(
    RecordViewModel record, RecordingCoordinator coordinator, GlobalHotkeys hotkeys, IErrorReporter errors, IOsCapabilities os) : IDisposable
{
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private WinEventDelegate? _winEventProc; // kept alive for the hook's lifetime once assigned

    private ContourOverlayWindow? _overlay;
    private IntPtr _winEventHook;
    private IntPtr _followedHandle;
    private MonitorInfo? _currentMonitor;
    private int _clearRegionHotkeyId = -1;
    private bool _warnedUnsupportedThisDrag;
    private bool _loggedRetargetThisDrag;

    public void Attach()
    {
        _winEventProc = OnLocationChanged;
        record.PropertyChanged += OnPropertyChanged;
        hotkeys.Pressed += OnHotkeyPressed;
        Update();
    }

    private void OnLocationChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == _followedHandle && idObject == OBJID_WINDOW && idChild == CHILDID_SELF)
        {
            Update();
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RecordViewModel.IsRegionSource):
            case nameof(RecordViewModel.IsWindowSource):
            case nameof(RecordViewModel.IsScreenSource):
            case nameof(RecordViewModel.SelectedWindow):
            case nameof(RecordViewModel.SelectedMonitor):
            case nameof(RecordViewModel.RegionLabel):
            case nameof(RecordViewModel.IsRecording):
            case nameof(RecordViewModel.ActiveCaptureTarget):
            case nameof(RecordViewModel.IsActivePage):
            case nameof(RecordViewModel.IsWindowMinimized):
                Update();
                break;
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == _clearRegionHotkeyId)
        {
            record.ClearRegionSelection();
        }
    }

    private void Update()
    {
        // Authoritative recording state — record.IsRecording mirrors this via progress ticks and can lag
        // by up to one tick right after Start()/Stop(), which matters for a drag that starts the instant
        // recording begins; coordinator.IsRecording never has that delay.
        bool isRecording = coordinator.IsRecording;
        bool visible = isRecording || (record.IsActivePage && !record.IsWindowMinimized);
        CaptureTarget? target = visible
            ? (isRecording ? record.ActiveCaptureTarget : record.CurrentSelectionTarget)
            : null;

        if (target is not { Kind: CaptureKind.Region or CaptureKind.Window } ||
            !CaptureCapabilities.TryGetScreenBounds(target, out RegionRect bounds))
        {
            Hide();
            UpdateClearRegionHotkey(register: false);
            return;
        }

        _currentMonitor = target.Kind == CaptureKind.Region
            ? CaptureCapabilities.EnumerateMonitors().FirstOrDefault(m => m.Handle == target.Handle)
            : null;

        Show();
        _overlay!.SetInteractive(target.Kind == CaptureKind.Region);
        if (_currentMonitor is { } mon)
        {
            _overlay.SetMonitorBounds(new RegionRect(mon.X, mon.Y, mon.Width, mon.Height));
        }
        _overlay.UpdateBounds(bounds);
        _overlay.SetRecording(isRecording);

        if (target.Kind == CaptureKind.Window)
        {
            StartFollowing(target.Handle);
        }
        else
        {
            StopFollowing();
        }

        UpdateClearRegionHotkey(register: target.Kind == CaptureKind.Region && !isRecording);
    }

    private void UpdateClearRegionHotkey(bool register)
    {
        if (register && _clearRegionHotkeyId == -1)
        {
            _clearRegionHotkeyId = hotkeys.Register(0, VirtualKeys.Escape);
        }
        else if (!register && _clearRegionHotkeyId != -1)
        {
            hotkeys.Unregister(_clearRegionHotkeyId);
            _clearRegionHotkeyId = -1;
        }
    }

    private void Show()
    {
        if (_overlay is not null)
        {
            return;
        }

        _overlay = new ContourOverlayWindow(os);
        _overlay.BoundsDragged += OnBoundsDragged;
        _overlay.DragCompleted += OnDragCompleted;
        _overlay.Show();
    }

    /// <summary>Fires continuously while the contour is being dragged/resized — retargets the live capture
    /// (while recording) or the pending selection (beforehand) immediately, in source-local (monitor-local)
    /// pixels. Never persists or restarts the preview here — that's <see cref="OnDragCompleted"/>'s job, once
    /// the gesture actually ends, so a drag doesn't hammer disk or tear down/rebuild the preview mid-motion.</summary>
    private void OnBoundsDragged(RegionRect screenBounds)
    {
        if (_currentMonitor is not { } mon)
        {
            return;
        }

        var local = new RegionRect(screenBounds.X - mon.X, screenBounds.Y - mon.Y, screenBounds.Width, screenBounds.Height);
        if (coordinator.IsRecording)
        {
            if (!coordinator.CaptureSupportsZoom)
            {
                if (!_warnedUnsupportedThisDrag)
                {
                    _warnedUnsupportedThisDrag = true;
                    errors.Warn("record.contour-drag-unsupported", "Dragging the outline won't change this recording.",
                        "Screen capture fell back to a compatibility mode on this system, which can't retarget the captured area live.");
                }

                return;
            }

            if (!_loggedRetargetThisDrag)
            {
                _loggedRetargetThisDrag = true;
                Serilog.Log.Information("Contour drag retargeting the live recording to {Rect} (monitor-local)", local);
            }

            coordinator.SetBaseRect(local);
        }
        else
        {
            record.UpdateRegionFromDrag(local);
        }
    }

    private void OnDragCompleted()
    {
        _warnedUnsupportedThisDrag = false;
        _loggedRetargetThisDrag = false;
        if (!coordinator.IsRecording)
        {
            record.CompleteRegionDrag();
        }
    }

    private void Hide()
    {
        StopFollowing();
        if (_overlay is not null)
        {
            _overlay.BoundsDragged -= OnBoundsDragged;
            _overlay.DragCompleted -= OnDragCompleted;
            _overlay.Close();
            _overlay = null;
        }

        _currentMonitor = null;
    }

    private void StartFollowing(IntPtr handle)
    {
        if (_followedHandle == handle && _winEventHook != IntPtr.Zero)
        {
            return;
        }

        StopFollowing();
        _ = GetWindowThreadProcessId(handle, out uint pid);
        _followedHandle = handle;
        _winEventHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventProc!, pid, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void StopFollowing()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        _followedHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        hotkeys.Pressed -= OnHotkeyPressed;
        UpdateClearRegionHotkey(register: false);
        Hide();
    }
}
