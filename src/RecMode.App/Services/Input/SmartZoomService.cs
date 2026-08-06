using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.Capture;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>
/// Smart auto-zoom: while recording and the "Smart auto-zoom" setting is on, pans/zooms the GPU crop toward
/// each mouse click, then eases back out to the full frame after a short idle period (or immediately re-targets
/// on the next click). The actual per-frame crop interpolation lives on the GPU pipeline
/// (<see cref="RecMode.Capture"/>'s <c>VideoProcessorPipeline.SetZoomTarget</c>, applied every frame the same
/// way the brightness filter is) — this service only decides *when* to retarget, via
/// <see cref="RecordingCoordinator.ComputeZoomRect"/>, which documents the Monitor/Region-only v1 scope cut.
/// <para>
/// Owns a private <see cref="GlobalMouseHook"/> instance rather than sharing <see cref="ClickHighlightService"/>'s
/// DI singleton: both services independently call <c>Install()</c>/<c>Uninstall()</c> based on their own
/// setting, and <c>WH_MOUSE_LL</c> supports multiple simultaneous hooks per process, so a shared hook would mean
/// one feature's Uninstall could silently kill the other's clicks whenever the two settings are toggled
/// independently.
/// </para>
/// Lifecycle mirrors <see cref="ClickHighlightService"/> (§3.9): the hook and idle timer only exist while
/// recording and the setting is on, torn down otherwise.
/// </summary>
public sealed class SmartZoomService(RecordViewModel record, ISettingsService settings, RecordingCoordinator coordinator) : IDisposable
{
    private const double ZoomFactor = 1.8;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(2.5);

    private readonly GlobalMouseHook _hook = new();
    private Timer? _idleTimer;
    private bool _active;

    public void Attach()
    {
        record.PropertyChanged += OnPropertyChanged;
        settings.SettingsChanged += OnSettingsChanged;
        UpdateActive();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordViewModel.IsRecording))
        {
            return;
        }

        UpdateActive();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => UpdateActive();

    private void UpdateActive()
    {
        if (record.IsRecording && settings.Current.AutoZoomEnabled) Start();
        else Stop();
    }

    private void Start()
    {
        if (_active)
        {
            return;
        }

        _active = true;
        _hook.Clicked += OnClicked;
        _hook.Install();
    }

    // OnClicked runs synchronously ON the UI thread's own message dispatch, as part of the WH_MOUSE_LL hook
    // procedure (same shape as ClickHighlightService.OnClicked, see its comment) — Windows enforces a hook
    // timeout (LowLevelHooksTimeout, 300ms default) on that callback, and blocking it for too long makes
    // Windows silently unhook it, breaking auto-zoom AND (since ClickHighlightService uses a separate hook
    // instance) potentially the click-ripple highlight for the rest of the session with no error surfaced
    // anywhere. The real work here used to run inline: RecordingCoordinator.ComputeZoomRect ->
    // ResolveZoomMonitor -> CaptureCapabilities.EnumerateMonitors() creates a fresh IDXGIFactory1 and walks
    // every adapter/output (IsMonitorHdr's QueryInterface<IDXGIOutput6> per monitor) — real COM/DXGI cost,
    // on the multi-adapter-per-GPU shape this exact dev machine has — plus SetZoomTarget taking a lock and a
    // new Timer allocation, all still inside the hook proc. Deferred the same way ClickHighlightService
    // already does: BeginInvoke hands it to a later, separate pass through the dispatcher queue.
    private void OnClicked(int screenX, int screenY) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render, () => HandleClick(screenX, screenY));

    private void HandleClick(int screenX, int screenY)
    {
        // Both features drive the same GPU crop (RecordingCoordinator.SetZoomTarget) with no coordination
        // between them. Without this guard, clicking the toolbar's "Zoom" button and dragging out a manual
        // zoom region — both real clicks — would still arm this service's idle timer, which then silently
        // reset the crop to full frame ~2.5s later: the recording visibly un-zoomed while ZoomButtonText/
        // IsManualZooming stayed "Exit Zoom"/true, so the button and the Esc hotkey then toggled out of a
        // mode that wasn't actually active anymore.
        if (record.IsManualZooming)
        {
            return;
        }

        RegionRect? zoomRect = coordinator.ComputeZoomRect(screenX, screenY, ZoomFactor);
        if (zoomRect is null)
        {
            return; // unsupported source, or the click landed outside the captured area
        }

        coordinator.SetZoomTarget(zoomRect);
        _idleTimer?.Dispose();
        // Also re-checked here, not just in OnClicked above: a timer armed by an auto-zoom click can still be
        // pending when the user starts a manual zoom a moment later, and firing this unconditionally would
        // reset the crop out from under that manual session just the same as an unguarded click would.
        _idleTimer = new Timer(_ => { if (!record.IsManualZooming) coordinator.SetZoomTarget(null); }, null, IdleTimeout, Timeout.InfiniteTimeSpan);
    }

    private void Stop()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _hook.Uninstall();
        _hook.Clicked -= OnClicked;
        _idleTimer?.Dispose();
        _idleTimer = null;
        coordinator.SetZoomTarget(null);
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        settings.SettingsChanged -= OnSettingsChanged;
        Stop();
        _hook.Dispose();
    }
}
