using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>
/// Shows the floating recording toolbar while a recording is in progress and hides it when it ends, by
/// observing <see cref="RecordViewModel.IsRecording"/> (plan Phase 5). Decoupled from the start/stop flow so
/// it covers every path — manual stop, hotkey, CLI, or an error-driven finish.
/// </summary>
public sealed class RecordingToolbar(RecordViewModel record, IOsCapabilities os) : IDisposable
{
    private RecordingToolbarWindow? _window;

    public void Attach() => record.PropertyChanged += OnPropertyChanged;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordViewModel.IsRecording))
        {
            return;
        }

        if (record.IsRecording)
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
        if (_window is not null)
        {
            return;
        }

        _window = new RecordingToolbarWindow(record, os);
        _window.Show();
    }

    private void Hide()
    {
        _window?.Close();
        _window = null;
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        Hide();
    }
}
