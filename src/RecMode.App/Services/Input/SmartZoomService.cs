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

    private void OnClicked(int screenX, int screenY)
    {
        RegionRect? zoomRect = coordinator.ComputeZoomRect(screenX, screenY, ZoomFactor);
        if (zoomRect is null)
        {
            return; // unsupported source, or the click landed outside the captured area
        }

        coordinator.SetZoomTarget(zoomRect);
        _idleTimer?.Dispose();
        _idleTimer = new Timer(_ => coordinator.SetZoomTarget(null), null, IdleTimeout, Timeout.InfiniteTimeSpan);
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
