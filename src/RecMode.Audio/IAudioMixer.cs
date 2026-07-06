using System.IO.Pipes;

namespace RecMode.Audio;

/// <summary>
/// Captures system loopback + microphone, mixes to 48 kHz stereo f32 with per-source gain/mute and a
/// soft-clip sum (plan Phase 4). Runs while the Record screen is visible (for meters) or recording (for the
/// encoder), torn down otherwise (§3.9). Meters are computed on the audio thread and read by the UI ≤ 30 Hz.
/// </summary>
public interface IAudioMixer : IDisposable
{
    bool IsRunning { get; }
    int SampleRate { get; }
    int Channels { get; }

    bool SystemEnabled { get; }
    bool MicEnabled { get; }

    float SystemGain { get; set; }
    bool SystemMuted { get; set; }
    float MicGain { get; set; }
    bool MicMuted { get; set; }

    AudioLevel SystemLevel { get; }
    AudioLevel MicLevel { get; }

    /// <summary>
    /// Starts capture/metering for the requested sources. When <paramref name="targetProcessId"/> is set and
    /// <paramref name="captureSystem"/> is true, the system source captures only that process's (and by
    /// default its child processes') audio instead of the whole system — per-app audio (plan §7).
    /// </summary>
    void Start(bool captureSystem, bool captureMic, int? targetProcessId = null);

    void Stop();

    /// <summary>
    /// Writes mixed f32le to <paramref name="pipe"/> paced to <paramref name="activeElapsed"/> (the recording's
    /// active time, which excludes paused spans) so audio pauses in lockstep with video and stays synced;
    /// pads silence on underflow. Returns bytes written.
    /// </summary>
    long PumpUntil(NamedPipeServerStream pipe, Func<TimeSpan> activeElapsed, CancellationToken token);
}
