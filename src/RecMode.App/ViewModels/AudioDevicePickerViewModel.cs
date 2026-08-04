using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RecMode.Audio;

namespace RecMode.App.ViewModels;

/// <summary>One playback device row in the audio-device picker, with its own checked state.</summary>
public sealed class AudioDeviceRowViewModel(AudioRenderDeviceInfo info) : ObservableObject
{
    private bool _isSelected;

    public string Id => info.Id;
    public string Name => info.Name;
    public bool IsDefaultConsole => info.IsDefaultConsole;
    public bool IsDefaultCommunications => info.IsDefaultCommunications;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>
/// Edit model for the "System audio devices" picker (Settings → Record). Lets a user override the automatic
/// Console+Communications device detection with an explicit set of playback devices to loop back — e.g. to
/// exclude a device whose audio they don't want in the recording, or to add extra devices the auto-detect
/// wouldn't otherwise cover.
/// </summary>
public sealed class AudioDevicePickerViewModel : ObservableObject
{
    private bool _useAutomatic;

    public AudioDevicePickerViewModel(IReadOnlyList<AudioRenderDeviceInfo> devices, List<string>? selectedIds)
    {
        _useAutomatic = selectedIds is null;
        Devices = new ObservableCollection<AudioDeviceRowViewModel>(devices.Select(d =>
        {
            var row = new AudioDeviceRowViewModel(d);
            if (selectedIds is not null)
            {
                row.IsSelected = selectedIds.Contains(d.Id, StringComparer.Ordinal);
            }

            return row;
        }));
    }

    public ObservableCollection<AudioDeviceRowViewModel> Devices { get; }

    public bool UseAutomatic
    {
        get => _useAutomatic;
        set
        {
            if (SetProperty(ref _useAutomatic, value))
            {
                OnPropertyChanged(nameof(UseCustom));
            }
        }
    }

    /// <summary>Mirror of <see cref="UseAutomatic"/> for the "Custom" segmented option and the device list's
    /// enabled state — avoids needing a generic bool-inverter converter for what's otherwise a two-way binding.</summary>
    public bool UseCustom
    {
        get => !_useAutomatic;
        set => UseAutomatic = !value;
    }

    public bool HasDevices => Devices.Count > 0;

    /// <summary>The value to persist to <c>RecModeSettings.SystemAudioDeviceIds</c>: null for automatic,
    /// otherwise exactly the checked device IDs (possibly empty, if the user unchecked everything — a
    /// deliberate "record no system audio" choice, honored rather than silently reinterpreted as automatic).</summary>
    public List<string>? Result => UseAutomatic ? null : Devices.Where(d => d.IsSelected).Select(d => d.Id).ToList();
}
