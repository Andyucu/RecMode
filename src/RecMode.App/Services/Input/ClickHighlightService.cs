using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>
/// Shows the click-highlight ripple overlay + installs the global mouse hook while a recording is in progress
/// and the "Highlight mouse clicks" setting is on (plan Phase 8). Torn down when recording stops (§3.9), so
/// the hook and overlay only exist during a recording.
/// </summary>
public sealed class ClickHighlightService(RecordViewModel record, ISettingsService settings, GlobalMouseHook hook) : IDisposable
{
    private ClickRippleOverlay? _overlay;

    public void Attach() => record.PropertyChanged += OnPropertyChanged;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordViewModel.IsRecording))
        {
            return;
        }

        if (record.IsRecording && settings.Current.HighlightClicks)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void Show()
    {
        if (_overlay is not null)
        {
            return;
        }

        _overlay = new ClickRippleOverlay();
        _overlay.Show();
        hook.Clicked += OnClicked;
        hook.Install();
    }

    private void OnClicked(int x, int y) => _overlay?.AddRipple(x, y);

    private void Hide()
    {
        hook.Uninstall();
        hook.Clicked -= OnClicked;
        _overlay?.Close();
        _overlay = null;
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        Hide();
    }
}
