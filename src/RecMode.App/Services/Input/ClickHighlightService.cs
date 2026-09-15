using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Errors;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>
/// Shows the click-highlight ripple overlay + installs the global mouse hook while a recording is in progress
/// and the "Highlight mouse clicks" setting is on (plan Phase 8). Torn down when recording stops (§3.9), so
/// the hook and overlay only exist during a recording.
/// </summary>
public sealed class ClickHighlightService(RecordViewModel record, ISettingsService settings, GlobalMouseHook hook, IErrorReporter errors) : IDisposable
{
    private ClickRippleOverlay? _overlay;

    public void Attach()
    {
        record.PropertyChanged += OnPropertyChanged;
        settings.SettingsChanged += OnSettingsChanged;
        UpdateVisibility();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordViewModel.IsRecording))
        {
            return;
        }

        UpdateVisibility();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (record.IsRecording && settings.Current.HighlightClicks) Show();
        else Hide();
    }

    private void Show()
    {
        if (_overlay is not null)
        {
            return;
        }

        // Install BEFORE creating the overlay, and bail on failure: ripples are driven entirely by the mouse
        // hook, so without it the overlay would be a no-op. SetWindowsHookExW can genuinely fail (EDR/anti-
        // cheat drivers, the per-desktop hook limit) — previously this was silent for the whole recording.
        if (!hook.Install())
        {
            errors.Warn("record.click-hook-failed",
                "Click highlights can't be shown for this recording.",
                "Windows refused the global mouse hook (some security software blocks it). Recording continues without click highlights.");
            return;
        }

        try
        {
            _overlay = new ClickRippleOverlay(record.ActiveCaptureTarget);
            _overlay.Show();
        }
        catch
        {
            // The hook reference is already held at this point, and Hide()'s _overlay-null guard would
            // early-return without releasing it if the overlay constructor/show threw — leaking one refcount
            // on the shared hook for the rest of the process. Release it here and let the failure propagate.
            hook.Uninstall();
            _overlay = null;
            throw;
        }

        hook.Clicked += OnClicked;
        // Window-source recordings only see their own window's rendered content via WGC's per-window
        // capture — a separate top-level overlay window like this one is invisible to it otherwise, so
        // without this the ripple showed live on screen but never in the actual recording. See
        // RecordingCoordinator.SetClickHighlightActive's doc comment.
        record.NotifyClickHighlightActive(true);
    }

    // OnClicked runs synchronously ON the UI thread's own message dispatch, as part of the WH_MOUSE_LL hook
    // procedure itself (SetWindowsHookEx callbacks for a hook installed with dwThreadId=0/current-thread run
    // inline with that thread's message pump, not on a separate thread) — this callback IS the thing standing
    // between the OS and every other application's mouse input. AddRipple allocates a WPF Ellipse/
    // ScaleTransform/three DoubleAnimations and mutates the live visual tree; running that inline here delays
    // system-wide mouse processing for as long as it takes, and if the UI thread is ever also stalled on
    // something else (e.g. a synchronous screenshot capture), every click system-wide queues up behind it.
    // BeginInvoke defers the actual overlay work to a later, separate pass through the dispatcher queue, so
    // this hook procedure itself returns to the OS immediately.
    private void OnClicked(int x, int y) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render, () => _overlay?.AddRipple(x, y));

    private void Hide()
    {
        if (_overlay is null)
        {
            return;
        }

        hook.Uninstall();
        hook.Clicked -= OnClicked;
        _overlay?.Close();
        _overlay = null;
        record.NotifyClickHighlightActive(false);
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        settings.SettingsChanged -= OnSettingsChanged;
        Hide();
    }
}
