using RecMode.Capture.Webcam;

namespace RecMode.Capture;

/// <summary>
/// Live preview capture for the Record screen (plan §3.9): runs only while the screen is visible and not
/// recording, delivers scaled BGRA frames at ≤ 30 fps. Uses the latest-frame pull pattern (no per-frame
/// allocation on the delivery path) — the UI reads <see cref="TryGetLatestFrame"/> on <see cref="FrameAvailable"/>.
/// </summary>
public interface IPreviewEngine : IDisposable
{
    int Width { get; }
    int Height { get; }
    int Stride { get; }
    int ByteSize { get; }

    /// <summary>Raised (off-thread, ≤ 30 Hz) when a new frame is ready. Handlers marshal to the UI thread.</summary>
    event Action? FrameAvailable;

    /// <summary>Starts capturing <paramref name="target"/>, scaled to fit within <paramref name="maxWidth"/>×
    /// <paramref name="maxHeight"/> (aspect-preserving, never upscaled past the source's own size) — pass the
    /// preview surface's actual on-screen pixel size so this doesn't do GPU scale/readback/upload work for
    /// pixels larger than what will ever actually be displayed. Defaults match the size this always rendered
    /// at before the caller could report a real one.</summary>
    void Start(CaptureTarget target, bool captureCursor, int maxWidth = 1280, int maxHeight = 720);
    bool TryGetLatestFrame(byte[] dest);

    /// <summary>Enables/disables the webcam picture-in-picture overlay on the preview; call after <see cref="Start"/>. Null source disables it.</summary>
    void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect);

    /// <summary>Sets the captured-video brightness adjustment, -100..100, 0 = unchanged; call before or after <see cref="Start"/>.</summary>
    void SetBrightness(double value);

    /// <summary>Marks a source-pixel rect to blank out in the preview (live redaction, plan §7 privacy);
    /// null clears it. Preview-only visuals — the recording gate that actually enforces redaction lives in
    /// <c>RecordingCoordinator</c>, which refuses to record when it can't be applied.</summary>
    void SetRedaction(RegionRect? sourceRect);

    void Stop();
}
