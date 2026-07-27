using RecMode.Core.Recording;

namespace RecMode.App.Services;

/// <summary>What the pacer should do about encoder health this frame.</summary>
public enum PacerHealthAction
{
    /// <summary>Nothing to do — either keeping up, or behind but not yet past the Degraded threshold.</summary>
    None,

    /// <summary>Sustained behind real time: report a DegradedState warning (once per recording).</summary>
    Degrade,

    /// <summary>Still behind well past Degraded on a hardware encoder: rotate to software (once per recording).</summary>
    DowngradeToSoftware,
}

/// <summary>
/// The "is the encoder keeping up" hysteresis from <c>RecordingCoordinator.PaceLoop</c> (§3.6 recording
/// health), as pure state. Tracks how long the pacer has been behind real time, decides when that becomes a
/// Degraded warning and then a hardware→software downgrade, and — critically — knows when the clock must be
/// restarted so a legitimate pause in frame writes isn't mistaken for a slow encoder.
/// <para>Extracted specifically because this is demonstrably regression-prone: an auto-split rotation
/// blocking the pacer for the length of a finalize + safe-remux + encoder-restart used to be counted as
/// "the encoder has been behind for 3+ seconds", which could reach the downgrade threshold and swap out a
/// perfectly healthy hardware encoder on a slow remux alone. That was a real shipped bug, and no test could
/// reach the logic to catch it — it lived inline in a ~200-line loop body wired to real capture, a real
/// ffmpeg subprocess, and wall-clock timing.</para>
/// </summary>
public sealed class PacerHealthTracker
{
    private readonly long _ticksPerSecond;
    private readonly double _degradeAfterSeconds;

    // Nullable rather than a 0 sentinel: 0 is a legitimate timestamp, and using it as "not started" meant a
    // tracker whose first behind-frame landed on tick 0 could never start its clock at all.
    private long? _behindSince;
    private bool _degradeReported;

    /// <param name="ticksPerSecond">Frequency of the timestamps passed to <see cref="Evaluate"/> —
    /// <c>Stopwatch.Frequency</c> in production, an arbitrary value in tests.</param>
    /// <param name="degradeAfterSeconds">How long the pacer must stay behind before it counts as Degraded.</param>
    public PacerHealthTracker(long ticksPerSecond, double degradeAfterSeconds = 3.0)
    {
        _ticksPerSecond = ticksPerSecond > 0 ? ticksPerSecond : 1;
        _degradeAfterSeconds = degradeAfterSeconds;
    }

    /// <summary>True while the encoder is considered to be failing to keep up — surfaced in progress reports.</summary>
    public bool IsBehind { get; private set; }

    /// <summary>
    /// Restarts the behind-clock and clears the behind flag, without resetting whether a Degraded warning has
    /// already been reported. Call after anything that legitimately blocks frame writes for a while — an
    /// auto-split segment rotation, or a downgrade's own rotation — so that gap isn't attributed to encoder
    /// throughput. Forgetting this on the auto-split path is exactly the bug that shipped once already.
    /// </summary>
    public void ResetAfterRotation()
    {
        _behindSince = null;
        IsBehind = false;
    }

    /// <summary>
    /// Folds one frame's worth of progress into the health state and returns what the caller should do.
    /// <see cref="PacerHealthAction.Degrade"/> is returned at most once per recording; the caller reports the
    /// warning. <see cref="PacerHealthAction.DowngradeToSoftware"/> is returned whenever the downgrade
    /// threshold is met on a hardware encoder — the caller enforces "once per recording" (it already tracks
    /// whether a downgrade has been attempted) and calls <see cref="ResetAfterRotation"/> afterward.
    /// </summary>
    /// <param name="nowTicks">Current timestamp in <c>ticksPerSecond</c> units.</param>
    /// <param name="elapsedSeconds">Active recording time (excludes paused spans).</param>
    /// <param name="framesWritten">Frames actually handed to the encoder so far.</param>
    /// <param name="fps">Target frame rate.</param>
    /// <param name="encoderIsHardware">Whether the encoder currently in use is a hardware one.</param>
    public PacerHealthAction Evaluate(long nowTicks, double elapsedSeconds, long framesWritten, int fps, bool encoderIsHardware)
    {
        if (!RecordingHealth.IsBehindRealtime(elapsedSeconds, framesWritten, fps))
        {
            // Only clear once genuinely caught up — within half a second's worth of frames. Clearing the
            // moment the instantaneous check passes would flap on and off across a marginal encoder.
            if (RecordingHealth.FramesBehind(elapsedSeconds, framesWritten, fps) <= fps / 2)
            {
                _behindSince = null;
                IsBehind = false;
            }

            return PacerHealthAction.None;
        }

        if (_behindSince is not { } behindSince)
        {
            _behindSince = nowTicks;
            return PacerHealthAction.None;
        }

        double behindSeconds = (nowTicks - behindSince) / (double)_ticksPerSecond;
        if (behindSeconds <= _degradeAfterSeconds)
        {
            return PacerHealthAction.None;
        }

        IsBehind = true;

        // Degrade is always reported before any downgrade, even if the pacer somehow jumped straight past
        // the downgrade threshold — the user should always learn *why* the encoder got swapped. In practice
        // the two thresholds are seconds apart and the pacer evaluates every frame, so the downgrade simply
        // lands on a subsequent call.
        if (!_degradeReported)
        {
            _degradeReported = true;
            return PacerHealthAction.Degrade;
        }

        return RecordingHealth.ShouldDowngradeToSoftware(behindSeconds, encoderIsHardware)
            ? PacerHealthAction.DowngradeToSoftware
            : PacerHealthAction.None;
    }
}
