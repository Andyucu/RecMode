namespace RecMode.Capture;

/// <summary>
/// Screen capture producing tightly-packed NV12 frames for the encoding pipeline. Lives only while the
/// Record screen is visible or a recording is active (plan §3.9 lifecycle); <see cref="Start"/> spins up
/// WGC + the GPU NV12 converter, <see cref="Stop"/>/<see cref="IDisposable.Dispose"/> tears it all down.
/// </summary>
public interface ICaptureEngine : IDisposable
{
    bool IsRunning { get; }
    int OutputWidth { get; }
    int OutputHeight { get; }

    /// <summary>Tightly-packed NV12 frame size in bytes; the buffer passed to <see cref="TryGetLatestFrame"/> must be this big.</summary>
    int Nv12ByteSize { get; }

    /// <summary>Unique frames delivered by WGC so far (on-change; the pacer duplicates to CFR).</summary>
    long CapturedFrameCount { get; }

    /// <summary>Starts capturing <paramref name="monitor"/>, converting to NV12 scaled to <paramref name="dstW"/>×<paramref name="dstH"/>.</summary>
    void Start(MonitorInfo monitor, int dstW, int dstH);

    /// <summary>Copies the most recent NV12 frame into <paramref name="dest"/>. False until the first frame arrives.</summary>
    bool TryGetLatestFrame(byte[] dest);

    void Stop();
}

/// <summary>Static discovery that doesn't require an engine instance.</summary>
public static class CaptureCapabilities
{
    public static IReadOnlyList<MonitorInfo> EnumerateMonitors() => CaptureInterop.EnumerateMonitors();

    public static bool IsSupported() => Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();
}
