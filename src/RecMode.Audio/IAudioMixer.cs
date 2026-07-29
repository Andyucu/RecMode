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
    /// <paramref name="meteringOnly"/> — pass true when this mixer only ever feeds live UI meters and
    /// <see cref="PumpUntil"/> will never run against it (e.g. the Record screen's live meter bars, which
    /// run their own separate mixer instance from the one an actual recording pumps): each source then
    /// skips buffering samples for consumption entirely — peak/RMS are still computed every callback —
    /// instead of silently filling, then discarding out of, a buffer nobody was ever going to read.
    /// </summary>
    AudioMixerStartResult Start(bool captureSystem, bool captureMic, int? targetProcessId = null, bool meteringOnly = false);

    void Stop();

    /// <summary>
    /// Discards any audio already buffered from each active source, without stopping capture. Each source's
    /// <c>BufferedWaveProvider</c> starts accumulating the instant <see cref="Start"/> is called (or, across
    /// a pause, keeps accumulating the whole time capture is silently still running), independent of whether
    /// anything is actually consuming it yet — <see cref="PumpUntil"/> only starts pulling once the encoder's
    /// audio pipe has connected, which can be several seconds after <see cref="Start"/> (waiting on the video
    /// pipe/encoder to connect first), and doesn't run at all while paused. Without clearing here, that
    /// backlog is still sitting at the front of the buffer once consumption resumes, so <see cref="PumpUntil"/>'s
    /// first reads hand back *stale* audio — captured before recording (or resuming) actually began — instead
    /// of live current audio, silently shifting every real sample later than the video content it was
    /// actually captured alongside. Call this right before consumption is about to (re)start: once after
    /// <c>StartRecording()</c>, and once after every <c>Resume()</c>.
    /// </summary>
    void ClearBuffers();

    /// <summary>
    /// Writes mixed f32le to <paramref name="pipe"/> paced to <paramref name="segmentElapsed"/> (the current
    /// segment's active time, which excludes paused spans) so audio pauses in lockstep with video and stays synced;
    /// pads silence on underflow. Returns bytes written.
    /// <para>
    /// <paramref name="offsetMs"/> shifts the audio track relative to video, for the user-configurable A/V
    /// sync offset (<c>RecModeSettings.AudioSyncOffsetMs</c>). Positive delays audio (prepends that much
    /// silence — the correct direction when video lags audio); negative advances it (discards that much of
    /// the leading audio). Implemented here as literal samples rather than as an ffmpeg container-level
    /// timestamp shift (<c>-itsoffset</c>) deliberately: safe-recording remuxes MKV→MP4 with <c>-c copy</c>,
    /// and a container start-offset's survival across that rewrite is undocumented and player-dependent
    /// (MP4 expresses it as an edit list, which some players ignore outright). Real samples always survive.
    /// </para>
    /// </summary>
    long PumpUntil(NamedPipeServerStream pipe, Func<TimeSpan> segmentElapsed, CancellationToken token, int offsetMs = 0);
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
