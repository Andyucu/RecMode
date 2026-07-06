using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.App.Views;

namespace RecMode.App.Services;

/// <summary>
/// Shows/hides the draw-on-screen annotation overlay based on <see cref="RecordViewModel.IsAnnotating"/>
/// (plan Phase 8). The overlay's Esc key routes back to <see cref="RecordViewModel.StopAnnotating"/>, which
/// flips the flag and hides it. Torn down when annotation (or the recording) ends.
/// </summary>
public sealed class AnnotationService(RecordViewModel record) : IDisposable
{
    private AnnotationOverlay? _overlay;

    public void Attach() => record.PropertyChanged += OnPropertyChanged;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordViewModel.IsAnnotating))
        {
            return;
        }

        if (record.IsAnnotating)
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

        _overlay = new AnnotationOverlay(record.StopAnnotating);
        _overlay.Show();
    }

    private void Hide()
    {
        _overlay?.Close();
        _overlay = null;
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        Hide();
    }
}
