namespace RecMode.Capture.Webcam;

/// <summary>
/// Pull-based access to the most recent webcam frame (BGRA8), for the GPU compositor to upload on its own
/// cadence — mirrors the "latest frame" pattern already used for WGC capture/preview (no queueing, no
/// per-frame allocation on the delivery side).
/// </summary>
public interface IWebcamFrameSource
{
    /// <summary>
    /// Copies the most recent frame into <paramref name="destination"/>, reallocating it only when it's too
    /// small (so a steady-state caller allocates nothing). Returns false until the first frame arrives.
    /// <para>The caller owns <paramref name="destination"/> — this deliberately does not hand back the
    /// source's own live buffer. The capture callback overwrites that buffer in place as each new camera
    /// frame arrives, so a consumer reading it outside the lock (a full-frame NV12 convert or BGRA scale
    /// takes several ms; frames arrive every ~33 ms) would read top rows from one frame and bottom rows
    /// from the next — a torn frame, on the recording, preview, picture-in-picture, and screenshot paths
    /// simultaneously. Copying under the lock costs one memcpy and removes the tear entirely.</para>
    /// </summary>
    bool TryGetLatestFrame(ref byte[] destination, out int width, out int height, out int stride);
}
