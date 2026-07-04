namespace RecMode.Capture;

/// <summary>
/// Live preview capture for the Record screen (plan §3.9): runs only while the screen is visible and not
/// recording, delivers scaled BGRA frames at ≤ 30 fps. Uses the latest-frame pull pattern (no per-frame
/// allocation on the delivery path) — the UI reads <see cref="TryGetLatestFrame"/> on <see cref="FrameAvailable"/>.
/// </summary>
public interface IPreviewEngine : IDisposable
{
    bool IsRunning { get; }
    int Width { get; }
    int Height { get; }
    int Stride { get; }
    int ByteSize { get; }

    /// <summary>Raised (off-thread, ≤ 30 Hz) when a new frame is ready. Handlers marshal to the UI thread.</summary>
    event Action? FrameAvailable;

    void Start(CaptureTarget target, bool captureCursor);
    bool TryGetLatestFrame(byte[] dest);
    void Stop();
}
