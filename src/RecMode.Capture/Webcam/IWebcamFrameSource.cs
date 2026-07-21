namespace RecMode.Capture.Webcam;

/// <summary>
/// Pull-based access to the most recent webcam frame (BGRA8), for the GPU compositor to upload on its own
/// cadence — mirrors the "latest frame" pattern already used for WGC capture/preview (no queueing, no
/// per-frame allocation on the delivery side).
/// </summary>
public interface IWebcamFrameSource
{
    bool TryGetLatestFrame(out byte[] data, out int width, out int height, out int stride);
}
