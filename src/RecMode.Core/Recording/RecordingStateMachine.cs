namespace RecMode.Core.Recording;

/// <summary>
/// The single recording state machine (plan §3.7). Owns legal transitions, the record→pause→finalize
/// lifecycle, and the pause PTS math that keeps output gapless. This is deliberately UI-free and
/// side-effect-free beyond raising <see cref="StateChanged"/>: capture/audio/encoder subsystems subscribe
/// and react. Not thread-safe — callers marshal onto one thread (the UI/dispatcher).
/// </summary>
public sealed class RecordingStateMachine
{
    private readonly IMonotonicClock _clock;
    private readonly Lock _sync = new();

    // Wall-clock (monotonic) anchors for the active recording timeline.
    private TimeSpan _recordingStartedAt;
    private TimeSpan _pausedAt;
    private TimeSpan _totalPaused;
    private TimeSpan _finalizingAt;
    private RecordingState _state = RecordingState.Idle;

    public RecordingStateMachine(IMonotonicClock? clock = null)
    {
        _clock = clock ?? new StopwatchClock();
    }

    public RecordingState State
    {
        get { lock (_sync) { return _state; } }
    }

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;

    /// <summary>True once recording has begun and not yet finalized.</summary>
    public bool IsActive => State is RecordingState.Recording or RecordingState.Paused;

    /// <summary>True while any recording resources are owned, including container finalization.</summary>
    public bool IsBusy => State != RecordingState.Idle;

    /// <summary>
    /// Active recorded duration excluding paused spans — the value a timer UI should show and the basis
    /// for media PTS. Zero until <see cref="Recording"/> is first entered.
    /// </summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_sync)
            {
                if (_state == RecordingState.Idle)
                {
                    return TimeSpan.Zero;
                }

                TimeSpan reference = _state switch
                {
                    RecordingState.Paused => _pausedAt,
                    RecordingState.Finalizing => _finalizingAt,
                    _ => _clock.Elapsed,
                };
                TimeSpan elapsed = reference - _recordingStartedAt - _totalPaused;
                return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            }
        }
    }

    /// <summary>
    /// Maps a raw capture timestamp (on the same monotonic timeline as the clock) to a gapless media
    /// PTS by subtracting the recording start and all accumulated paused time. Frames captured while
    /// paused should not be submitted; if one is, it collapses onto the pause boundary.
    /// </summary>
    public TimeSpan ToMediaPts(TimeSpan captureTimestamp)
    {
        lock (_sync)
        {
            TimeSpan cutoff = _state switch
            {
                RecordingState.Paused => _pausedAt,
                RecordingState.Finalizing => _finalizingAt,
                _ => captureTimestamp,
            };
            TimeSpan effective = captureTimestamp < cutoff ? captureTimestamp : cutoff;
            TimeSpan pts = effective - _recordingStartedAt - _totalPaused;
            return pts < TimeSpan.Zero ? TimeSpan.Zero : pts;
        }
    }

    /// <summary>Idle → Recording. Resets all timeline anchors for a fresh session and anchors the start to now.</summary>
    public void StartRecording()
    {
        lock (_sync)
        {
            Require(RecordingState.Idle);
            _recordingStartedAt = _clock.Elapsed;
            _pausedAt = default;
            _totalPaused = default;
            _finalizingAt = default;
            Transition(RecordingState.Recording);
        }
    }

    /// <summary>Recording → Paused. Freezes the active-time reference.</summary>
    public void Pause()
    {
        lock (_sync)
        {
            Require(RecordingState.Recording);
            _pausedAt = _clock.Elapsed;
            Transition(RecordingState.Paused);
        }
    }

    /// <summary>Paused → Recording. Adds the paused span to the running total so PTS stays gapless.</summary>
    public void Resume()
    {
        lock (_sync)
        {
            Require(RecordingState.Paused);
            _totalPaused += _clock.Elapsed - _pausedAt;
            Transition(RecordingState.Recording);
        }
    }

    /// <summary>Recording/Paused → Finalizing (flush, faststart/remux, library entry, toast).</summary>
    public void Stop()
    {
        lock (_sync)
        {
            if (_state is not (RecordingState.Recording or RecordingState.Paused))
            {
                throw InvalidTransition(nameof(Stop));
            }

            // If stopping while paused, close out the final paused span so Elapsed is stable post-stop.
            if (_state == RecordingState.Paused)
            {
                _totalPaused += _clock.Elapsed - _pausedAt;
            }

            _finalizingAt = _clock.Elapsed;
            Transition(RecordingState.Finalizing);
        }
    }

    /// <summary>Finalizing → Idle (finalization complete).</summary>
    public void CompleteFinalization()
    {
        lock (_sync)
        {
            Require(RecordingState.Finalizing);
            Transition(RecordingState.Idle);
        }
    }

    private void Require(RecordingState expected)
    {
        if (_state != expected)
        {
            throw InvalidTransition($"expected {expected}");
        }
    }

    private InvalidOperationException InvalidTransition(string what) =>
        new($"Invalid recording transition from {_state} ({what}).");

    private void Transition(RecordingState next)
    {
        RecordingState previous = _state;
        if (previous == next)
        {
            return;
        }

        _state = next;
        StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(previous, next));
    }
}
