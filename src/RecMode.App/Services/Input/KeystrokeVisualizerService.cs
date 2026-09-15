using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Errors;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>
/// Shows the keystroke-visualizer overlay + installs the global keyboard hook while a recording is in
/// progress and the "Show keystrokes" setting is on — mirrors <see cref="ClickHighlightService"/>. Torn down
/// when recording stops (§3.9), so the hook and overlay only exist during a recording.
/// </summary>
public sealed class KeystrokeVisualizerService(RecordViewModel record, ISettingsService settings, GlobalKeyboardHook hook, IErrorReporter errors) : IDisposable
{
    private KeystrokeOverlayWindow? _overlay;

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
        if (record.IsRecording && settings.Current.ShowKeystrokes) Show();
        else Hide();
    }

    private void Show()
    {
        if (_overlay is not null)
        {
            return;
        }

        // Install BEFORE creating the overlay, and bail on failure: without the keyboard hook the overlay
        // would sit on screen forever showing nothing. SetWindowsHookExW can genuinely fail (EDR/anti-cheat
        // drivers, the per-desktop hook limit), and this was previously silent for the entire recording.
        if (!hook.Install())
        {
            errors.Warn("record.keystroke-hook-failed",
                "Keystrokes can't be shown for this recording.",
                "Windows refused the global keyboard hook (some security software blocks it). Recording continues without the keystroke display.");
            return;
        }

        try
        {
            _overlay = new KeystrokeOverlayWindow(record.ActiveCaptureTarget);
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

        hook.KeyDown += OnKeyDown;
        // See ClickHighlightService.Show()'s identical comment — this overlay needs the same Window-source
        // substitution or it would show live on screen but never appear in the recording.
        record.NotifyKeystrokeVisualizerActive(true);
    }

    // OnKeyDown runs synchronously ON the UI thread's own message dispatch, as part of the WH_KEYBOARD_LL
    // hook procedure itself — see ClickHighlightService.OnClicked's identical comment for why. ShowCombo
    // builds two DoubleAnimationUsingKeyFrames and starts three animations on the live overlay window;
    // running that inline here would delay system-wide keyboard processing for as long as it takes.
    // KeystrokeFormatter.Format is cheap pure string formatting and stays inline; only the actual overlay
    // work is deferred to a later, separate dispatcher pass so this hook procedure returns immediately.
    private void OnKeyDown(uint vk, bool ctrl, bool alt, bool shift, bool win)
    {
        string? combo = KeystrokeFormatter.Format(vk, ctrl, alt, shift, win);
        if (combo is not null)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Render, () => _overlay?.ShowCombo(combo));
        }
    }

    private void Hide()
    {
        // Same guard as ClickHighlightService.Hide(): UpdateVisibility() calls Hide() on every
        // settings-change/no-op transition, not just on real teardown, and Uninstall() must stay balanced
        // with a successful Install() (this is a refcounted shared hook — an unbalanced release would
        // unhook it out from under the other owner).
        if (_overlay is null)
        {
            return;
        }

        hook.Uninstall();
        hook.KeyDown -= OnKeyDown;
        _overlay?.Close();
        _overlay = null;
        record.NotifyKeystrokeVisualizerActive(false);
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        settings.SettingsChanged -= OnSettingsChanged;
        Hide();
    }
}
