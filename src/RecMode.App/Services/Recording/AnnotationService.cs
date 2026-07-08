using System.ComponentModel;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>
/// Shows/hides the draw-on-screen annotation overlay based on <see cref="RecordViewModel.IsAnnotating"/>
/// (plan Phase 8). The overlay's Esc key routes back to <see cref="RecordViewModel.StopAnnotating"/>, which
/// flips the flag and hides it; a small capture-excluded <see cref="AnnotationHintWindow"/> shown alongside
/// it offers the same exit as a real button, since the ink overlay itself can't host visible chrome without
/// that chrome ending up in the recording. Torn down when annotation (or the recording) ends.
/// </summary>
public sealed class AnnotationService(RecordViewModel record, IOsCapabilities os) : IDisposable
{
    private AnnotationOverlay? _overlay;
    private AnnotationHintWindow? _hint;

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

        // Shown after the overlay so it lands above the full-screen ink surface in the topmost z-order.
        _hint = new AnnotationHintWindow(record.StopAnnotating, os);
        _hint.Show();
    }

    private void Hide()
    {
        _overlay?.Close();
        _overlay = null;
        _hint?.Close();
        _hint = null;
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        Hide();
    }
}
