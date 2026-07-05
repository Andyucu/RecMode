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
}
