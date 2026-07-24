using System.ComponentModel;
using RecMode.App.ViewModels;

namespace RecMode.App.Services;

/// <summary>
/// Registers a global Esc hotkey while <see cref="RecordViewModel.IsManualZooming"/> is true, so exiting the
/// toolbar's manual Zoom mode works regardless of which window currently has keyboard focus — mirrors
/// <see cref="AnnotationService"/>'s Esc handling for draw-on-screen annotation. Unlike annotation, manual
/// zoom has no full-screen overlay while active (only during the brief drag-select pick, which
/// <see cref="Views.RegionSelectWindow"/> already handles its own Esc-to-cancel for), so this only needs the
/// exit-while-zoomed case.
/// </summary>
public sealed class ManualZoomService(RecordViewModel record, GlobalHotkeys hotkeys) : IDisposable
{
    private int _escapeHotkeyId = -1;

    public void Attach()
    {
        record.PropertyChanged += OnPropertyChanged;
        hotkeys.Pressed += OnHotkeyPressed;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordViewModel.IsManualZooming))
        {
            return;
        }

        if (record.IsManualZooming)
        {
            if (_escapeHotkeyId == -1)
            {
                _escapeHotkeyId = hotkeys.Register(0, VirtualKeys.Escape);
            }
        }
        else if (_escapeHotkeyId != -1)
        {
            hotkeys.Unregister(_escapeHotkeyId);
            _escapeHotkeyId = -1;
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == _escapeHotkeyId)
        {
            record.StopManualZoom();
        }
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        hotkeys.Pressed -= OnHotkeyPressed;
        if (_escapeHotkeyId != -1)
        {
            hotkeys.Unregister(_escapeHotkeyId);
            _escapeHotkeyId = -1;
        }
    }
}
