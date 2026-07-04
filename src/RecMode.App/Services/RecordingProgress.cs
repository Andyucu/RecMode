using RecMode.Core.Recording;

namespace RecMode.App.Services;

/// <summary>Throttled snapshot pushed to the Record screen while recording (≤ 4 Hz, plan §3.9).</summary>
public sealed record RecordingProgress(
    RecordingState State, TimeSpan Elapsed, double Fps, long FramesWritten, double Mbps, long FileSizeBytes);
