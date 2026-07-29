namespace RecMode.Capture;

/// <summary>
/// Decides whether a just-arrived frame should be converted/processed now, given a target frame rate — used
/// to throttle WGC's <c>FrameArrived</c> (fires up to the source's own refresh rate) and DDA's
/// <c>AcquireNextFrame</c> down to the recording/preview's actual target fps.
/// <para>
/// Anchored to a fixed schedule grid — each accepted frame's next due time is the <em>previous</em> due time
/// plus one interval — rather than "time since the last accepted frame." The latter requires a full interval
/// to elapse from the exact instant of the last acceptance, which fails at matching rates: e.g. a 60fps target
/// on a 60Hz source delivers frames at ~16.6ms ± 1ms of ordinary vblank/dispatch jitter, and a strict "one full
/// interval since last accept" floor alternately accepts and rejects successive frames, yielding roughly half
/// the target rate instead of the requested one. A grid-anchored due time only needs "at or past the next
/// scheduled tick," which ordinary jitter satisfies on nearly every frame.
/// </para>
/// </summary>
public sealed class FrameRateLimiter
{
    private readonly long _ticksPerSecond;
    private long _intervalTicks;
    private long _nextDueTicks;

    public FrameRateLimiter(long ticksPerSecond) => _ticksPerSecond = ticksPerSecond > 0 ? ticksPerSecond : 1;

    /// <summary>Sets the target rate (0 = unthrottled) and resets the schedule.</summary>
    public void SetTargetFps(int fps)
    {
        _intervalTicks = fps > 0 ? _ticksPerSecond / fps : 0;
        _nextDueTicks = 0;
    }

    /// <summary>True if a frame arriving at <paramref name="nowTicks"/> should be accepted; false if it's too
    /// early relative to the target rate and should be skipped. The caller's own frame-pool/duplication call
    /// that produced the candidate frame must still happen regardless of the result, to keep that pool/
    /// duplication cycling normally.</summary>
    public bool ShouldAccept(long nowTicks)
    {
        if (_intervalTicks <= 0)
        {
            return true; // unthrottled
        }

        if (_nextDueTicks != 0 && nowTicks < _nextDueTicks)
        {
            return false;
        }

        long baseTicks = _nextDueTicks == 0 ? nowTicks : _nextDueTicks;
        _nextDueTicks = baseTicks + _intervalTicks;
        if (_nextDueTicks < nowTicks)
        {
            // Fell behind (e.g. a GPU stall) — don't leave the schedule in the past, which would otherwise
            // accept a burst of frames unthrottled while it catches back up.
            _nextDueTicks = nowTicks + _intervalTicks;
        }

        return true;
    }
}
