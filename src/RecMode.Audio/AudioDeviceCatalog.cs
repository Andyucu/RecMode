using NAudio.CoreAudioApi;
using Serilog;

namespace RecMode.Audio;

/// <summary>One active playback (render) device, for the Settings audio-device picker — lets a user see
/// exactly which physical outputs "System audio" would loop back and include/exclude specific ones.</summary>
public sealed record AudioRenderDeviceInfo(string Id, string Name, bool IsDefaultConsole, bool IsDefaultCommunications);

/// <summary>Enumerates active playback devices (not a hot path — called only when the picker UI opens).</summary>
public static class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioRenderDeviceInfo> EnumerateActiveRenderDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultConsoleId = TryGetDefaultId(enumerator, Role.Console);
            string? defaultCommsId = TryGetDefaultId(enumerator, Role.Communications);

            MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var result = new List<AudioRenderDeviceInfo>(devices.Count);
            foreach (MMDevice device in devices)
            {
                using (device)
                {
                    result.Add(new AudioRenderDeviceInfo(
                        device.ID,
                        device.FriendlyName,
                        string.Equals(device.ID, defaultConsoleId, StringComparison.Ordinal),
                        string.Equals(device.ID, defaultCommsId, StringComparison.Ordinal)));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Render-device enumeration failed; audio device picker will show no devices");
            return [];
        }
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            return device.ID;
        }
        catch (Exception)
        {
            // No default configured for this role (or no render devices at all) — not an error, just means
            // no device gets flagged as the default for that role.
            return null;
        }
    }
}
