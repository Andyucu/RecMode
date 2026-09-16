namespace RecMode.Capture.Webcam;

/// <summary>
/// <see cref="IPreviewEngine"/> for webcam-as-source (see <see cref="WebcamCaptureEngine"/>'s doc comment for
/// why this is a separate engine from the WGC-based default). CPU nearest-neighbor scale to fit the preview
/// surface, same "no GPU needed at webcam resolution/frame rate" reasoning as the recording-path engine.
/// </summary>
public sealed class WebcamPreviewEngine : IPreviewEngine
{
    private const int TargetFps = 30; // matches WgcPreviewEngine

    private readonly Lock _sync = new();
    private readonly FrameRateLimiter _rateLimiter = new(System.Diagnostics.Stopwatch.Frequency);
    private WebcamCaptureSource? _source;
    private Thread? _thread;
    private volatile bool _stopping;
    private byte[] _latest = [];
    private byte[] _scratch = [];
    private bool _hasLatest;
    private int _maxWidth = 1280, _maxHeight = 720;

    public bool IsRunning { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Stride { get; private set; }
    public int ByteSize { get; private set; }

    public event Action? FrameAvailable;

    public void Start(CaptureTarget target, bool captureCursor, int maxWidth = 1280, int maxHeight = 720)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRunning)
        {
            Stop();
        }
        if (target.WebcamDeviceId is not { } deviceId)
        {
            throw new InvalidOperationException("A webcam source requires a device id.");
        }

        _maxWidth = Math.Max(2, maxWidth);
        _maxHeight = Math.Max(2, maxHeight);

        var source = new WebcamCaptureSource();
        try
        {
            source.StartAsync(deviceId).GetAwaiter().GetResult();
        }
        catch
        {
            source.Stop();
            throw;
        }

        int srcW = Math.Max(2, source.NativeWidth), srcH = Math.Max(2, source.NativeHeight);
        (int dstW, int dstH) = FitPreview(srcW, srcH);
        Width = dstW;
        Height = dstH;
        Stride = dstW * 4;
        ByteSize = Stride * dstH;
        _latest = new byte[ByteSize];
        _scratch = new byte[ByteSize];
        _hasLatest = false;

        _source = source;
        _stopping = false;
        IsRunning = true;
        _rateLimiter.SetTargetFps(TargetFps);
        _thread = new Thread(PollLoop) { IsBackground = true, Name = "recmode-webcam-preview" };
        _thread.Start();
    }

    /// <summary>Bounded so teardown is prompt; not a polling interval — see <see cref="PollLoop"/>.</summary>
    private const int FrameWaitMs = 100;

    private void PollLoop()
    {
        WebcamCaptureSource source = _source!;
        byte[] bgra = []; // grown on first frame by TryGetLatestFrame, then reused — this thread owns it

        // Event-driven rather than polled (§3.9) — same change as WebcamCaptureEngine.PollLoop; see its
        // comment. Waiting on the camera's own frame event means no wakeups and no redundant rescaling
        // between frames, and the ≤30 fps throttle below still caps a fast camera.
        using var newFrame = new AutoResetEvent(false);
        void OnSourceFrameArrived()
        {
            try
            {
                newFrame.Set();
            }
            catch (ObjectDisposedException)
            {
                // Same guard as WebcamCaptureEngine.PollLoop, for the identical reason: WinRT's
                // MediaFrameReader.FrameArrived unsubscription below isn't guaranteed to block until an
                // already-in-flight callback on the camera's own thread finishes — that callback can still
                // reach here and call Set() a moment after this method's `finally` has unsubscribed and the
                // `using` above has disposed newFrame. Harmless to drop: this loop has already exited by the
                // time that can happen, so there's nothing left to wake up.
            }
        }
        source.FrameArrived += OnSourceFrameArrived;
        try
        {
            while (!_stopping)
            {
                bool signaled = newFrame.WaitOne(FrameWaitMs);
                if (!signaled && _hasLatest)
                {
                    continue; // nothing new since the last scale
                }

                if (!source.TryGetLatestFrame(ref bgra, out int w, out int h, out int _) || w <= 0 || h <= 0)
                {
                    continue;
                }

                if (!_rateLimiter.ShouldAccept(System.Diagnostics.Stopwatch.GetTimestamp()))
                {
                    continue;
                }

                ScaleBgra(bgra, w, h, Width, Height, _scratch);
                lock (_sync)
                {
                    (_scratch, _latest) = (_latest, _scratch);
                    _hasLatest = true;
                }
                FrameAvailable?.Invoke();
            }
        }
        catch (Exception)
        {
            // Preview is best-effort — the record path doesn't depend on it.
        }
        finally
        {
            source.FrameArrived -= OnSourceFrameArrived;
        }
    }

    private static void ScaleBgra(byte[] src, int srcW, int srcH, int dstW, int dstH, byte[] dst)
    {
        int srcStride = srcW * 4;
        int dstStride = dstW * 4;
        for (int y = 0; y < dstH; y++)
        {
            int sy = Math.Min(srcH - 1, y * srcH / dstH);
            for (int x = 0; x < dstW; x++)
            {
                int sx = Math.Min(srcW - 1, x * srcW / dstW);
                int si = sy * srcStride + sx * 4;
                int di = y * dstStride + x * 4;
                dst[di] = src[si]; dst[di + 1] = src[si + 1]; dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
            }
        }
    }

    public bool TryGetLatestFrame(byte[] dest)
    {
        lock (_sync)
        {
            if (!_hasLatest)
            {
                return false;
            }
            Buffer.BlockCopy(_latest, 0, dest, 0, ByteSize);
            return true;
        }
    }

    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect) { } // nonsensical when the webcam already IS the source
    public void SetBrightness(double value) { } // no GPU pass to apply it — see WebcamCaptureEngine's doc comment
    public void SetRedaction(RegionRect? sourceRect) { } // preview-only visual; the recording gate blocks this path

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }
        IsRunning = false;
        _stopping = true;
        _thread?.Join(2000);
        _thread = null;
        _source?.Stop();
        _source = null;
        _hasLatest = false;
    }

    public void Dispose() => Stop();

    private (int, int) FitPreview(int srcW, int srcH) =>
        PreviewSizing.Fit(srcW, srcH, _maxWidth, _maxHeight);
}
