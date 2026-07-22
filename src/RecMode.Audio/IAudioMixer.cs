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
    /// Returns which of the requested sources actually started, so callers can surface a recoverable
    /// warning when a source silently degraded instead of just continuing without audio.
    /// </summary>
    AudioMixerStartResult Start(bool captureSystem, bool captureMic, int? targetProcessId = null);

    void Stop();

    /// <summary>
    /// Writes mixed f32le to <paramref name="pipe"/> paced to <paramref name="segmentElapsed"/> (the current
    /// segment's active time, which excludes paused spans) so audio pauses in lockstep with video and stays synced;
    /// pads silence on underflow. Returns bytes written.
    /// </summary>
    long PumpUntil(NamedPipeServerStream pipe, Func<TimeSpan> segmentElapsed, CancellationToken token);
}

/// <summary>
/// Outcome of <see cref="IAudioMixer.Start"/>: which sources were requested vs. actually started.
/// Recording always continues even when a source fails to start (§ audio degrades gracefully), but the
/// caller can use this to tell the user which source silently dropped out instead of staying quiet about it.
/// </summary>
public sealed record AudioMixerStartResult
{
    public required bool SystemRequested { get; init; }
    public required bool SystemStarted { get; init; }
    public required bool MicRequested { get; init; }
    public required bool MicStarted { get; init; }

    /// <summary>True if a requested source failed to start (i.e. it degraded rather than being started).</summary>
    public bool SystemDegraded => SystemRequested && !SystemStarted;
    public bool MicDegraded => MicRequested && !MicStarted;
}
