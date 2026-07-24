using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>
/// Shows the keystroke-visualizer overlay + installs the global keyboard hook while a recording is in
/// progress and the "Show keystrokes" setting is on — mirrors <see cref="ClickHighlightService"/>. Torn down
/// when recording stops (§3.9), so the hook and overlay only exist during a recording.
/// </summary>
public sealed class KeystrokeVisualizerService(RecordViewModel record, ISettingsService settings, GlobalKeyboardHook hook) : IDisposable
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

        _overlay = new KeystrokeOverlayWindow();
        _overlay.Show();
        hook.KeyDown += OnKeyDown;
        hook.Install();
    }

    private void OnKeyDown(uint vk, bool ctrl, bool alt, bool shift, bool win)
    {
        string? combo = KeystrokeFormatter.Format(vk, ctrl, alt, shift, win);
        if (combo is not null)
        {
            _overlay?.ShowCombo(combo);
        }
    }

    private void Hide()
    {
        hook.Uninstall();
        hook.KeyDown -= OnKeyDown;
        _overlay?.Close();
        _overlay = null;
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        settings.SettingsChanged -= OnSettingsChanged;
        Hide();
    }
}
