using NAudio.CoreAudioApi;

namespace RecMode.Audio;

/// <summary>Cheap, one-shot check for whether any microphone is connected (used to pick a sensible
/// first-run default for the Mic toggle, not called on any hot path).</summary>
public static class MicrophoneDevices
{
    public static bool IsAnyConnected()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            return devices.Count > 0;
        }
        catch (Exception)
        {
            // No audio subsystem / enumeration failure — treat as "no mic" (fail closed, matches the
            // existing default) rather than letting a first-run check crash startup.
            return false;
        }
    }
}
