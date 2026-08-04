using System.Windows;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Audio;

namespace RecMode.App.Services;

/// <summary>Shows the modal "System audio devices" picker. Returns the new value for
/// <c>RecModeSettings.SystemAudioDeviceIds</c> (null = automatic), or a sentinel meaning "cancelled — leave
/// the setting unchanged" (distinguished from "cancelled" via <paramref name="changed"/> in
/// <see cref="TryPick"/> since null is itself a valid, meaningful result).</summary>
public interface IAudioDevicePrompt
{
    bool TryPick(List<string>? currentSelection, out List<string>? result);
}

/// <summary>Default <see cref="IAudioDevicePrompt"/> — a modal <see cref="AudioDevicePickerWindow"/> over the
/// live-enumerated render-device list.</summary>
public sealed class AudioDevicePrompt : IAudioDevicePrompt
{
    public bool TryPick(List<string>? currentSelection, out List<string>? result)
    {
        IReadOnlyList<AudioRenderDeviceInfo> devices = AudioDeviceCatalog.EnumerateActiveRenderDevices();
        var model = new AudioDevicePickerViewModel(devices, currentSelection);
        var window = new AudioDevicePickerWindow(model);
        if (Application.Current?.MainWindow is { IsVisible: true } main)
        {
            window.Owner = main;
        }

        if (window.ShowDialog() == true)
        {
            result = model.Result;
            return true;
        }

        result = null;
        return false;
    }
}
