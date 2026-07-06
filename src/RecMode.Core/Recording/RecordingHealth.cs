namespace RecMode.Core.Recording;

/// <summary>
/// Pure heuristic behind the recording health indicator (plan §3.6). The CFR pacer targets
/// <c>elapsed·fps</c> frames; if the encoder can't keep up, writes back-pressure and the written frame count
/// falls behind real time. More than one second behind (after a short grace period) is the "can't keep up"
/// signal the pacer treats, when sustained, as a Degraded state.
/// </summary>
public static class RecordingHealth
{
    /// <summary>How many frames behind real time the writer is (may be negative while catching up).</summary>
    public static long FramesBehind(double elapsedSeconds, long framesWritten, int fps)
        => (long)(elapsedSeconds * fps) - framesWritten;

    /// <summary>True once the writer is &gt; 1 s of frames behind real time (after a 2 s grace period).</summary>
    public static bool IsBehindRealtime(double elapsedSeconds, long framesWritten, int fps)
        => elapsedSeconds > 2 && FramesBehind(elapsedSeconds, framesWritten, fps) > fps;

    /// <summary>Free space at which a recording is stopped gracefully so a full disk can't corrupt the finish (§3.6).</summary>
    public const long DiskCriticalBytes = 500L * 1024 * 1024;

    /// <summary>True when free space has dropped to the critical threshold (ignores an unknown/negative reading).</summary>
    public static bool IsDiskCritical(long freeBytes) => freeBytes >= 0 && freeBytes < DiskCriticalBytes;

    /// <summary>How long (beyond the initial Degraded threshold) a hardware encoder must stay behind before falling back to software.</summary>
    public const double DowngradeAfterSeconds = 8.0;

    /// <summary>
    /// True once a <em>hardware</em> encoder has been continuously behind real time long enough that switching
    /// to software encoding (which trades CPU for keeping up) is worth the mid-recording segment rotation.
    /// Software encoders never trigger this — there's nowhere further to fall back to.
    /// </summary>
    public static bool ShouldDowngradeToSoftware(double behindDurationSeconds, bool encoderIsHardware)
        => encoderIsHardware && behindDurationSeconds > DowngradeAfterSeconds;

    /// <summary>
    /// Below this sustained write throughput, the output disk (not the encoder) is the likely bottleneck —
    /// e.g. a network share or an old flash drive. Deliberately conservative: even a slow 5400rpm HDD clears
    /// this by a wide margin, so it only fires for genuinely exotic targets, but a nearly-full-quota network
    /// share can have plenty of free *space* while still being far too slow — a gap the free-space pre-flight
    /// check alone can't catch.
    /// </summary>
    public const double DiskSlowThresholdMBps = 10.0;

    /// <summary>True when a measured write-speed probe came back under the slow-disk threshold (a negative reading means "unknown" — never warn on that).</summary>
    public static bool IsDiskTooSlow(double measuredMBps) => measuredMBps >= 0 && measuredMBps < DiskSlowThresholdMBps;
}
