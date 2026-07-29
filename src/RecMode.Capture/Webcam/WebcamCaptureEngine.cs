using System.Diagnostics;

namespace RecMode.Capture.Webcam;

/// <summary>
/// <see cref="ICaptureEngine"/> for recording a webcam directly as the video source (distinct from
/// <see cref="WebcamOverlayCompositor"/>, which composites a webcam onto another source's picture-in-picture
/// corner). Wraps <see cref="WebcamCaptureSource"/> and converts its BGRA8 frames to NV12 on the CPU via
/// <see cref="Bgra8ToNv12Converter"/> — a webcam's modest resolution/frame rate make this perfectly adequate
/// without needing its own D3D11 device/VideoProcessor, matching <see cref="GdiCaptureEngine"/>'s precedent.
/// Deliberate v1 scope cut, same class as other sources' own documented cuts: no GPU crop, so
/// <see cref="SupportsZoom"/> is false and brightness/zoom/base-rect are no-ops — smart auto-zoom and manual
/// zoom already handle that combination gracefully (a warning, not a crash).
/// </summary>
public sealed class WebcamCaptureEngine : ICaptureEngine
{
    private readonly Lock _sync = new();
    private WebcamCaptureSource? _source;
    private Thread? _thread;
    private volatile bool _stopping;
    private byte[] _latest = [];
    private byte[] _scratch = []; // converted into by the capture thread, then swapped with _latest
    private int _dstW, _dstH;
    private bool _hasLatest;
    private long _capturedFrames;
    private readonly FrameRateLimiter _rateLimiter = new(Stopwatch.Frequency);

    public bool IsRunning { get; private set; }
    public int OutputWidth => _dstW;
    public int OutputHeight => _dstH;
    public int Nv12ByteSize => _dstW * _dstH * 3 / 2;
    public long CapturedFrameCount => Interlocked.Read(ref _capturedFrames);
    public bool SupportsZoom => false;
    public bool HdrToneMapActive => false;

    public event EventHandler<Exception>? Faulted;

    public void Start(CaptureTarget target, int dstW, int dstH, bool captureCursor, int targetFps = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRunning)
        {
            throw new InvalidOperationException("Capture is already running.");
        }
        if (target.WebcamDeviceId is not { } deviceId)
        {
            throw new InvalidOperationException("A webcam source requires a device id.");
        }

        _dstW = dstW;
        _dstH = dstH;
        _rateLimiter.SetTargetFps(targetFps);
        _latest = new byte[Nv12ByteSize];
        _scratch = new byte[Nv12ByteSize];
        _hasLatest = false;
        _capturedFrames = 0;

        var source = new WebcamCaptureSource();
        try
        {
            // Start() is a synchronous contract (ICaptureEngine); WebcamCaptureSource.StartAsync must
            // complete before this returns so the very first PaceLoop iteration can already pull a frame.
            source.StartAsync(deviceId).GetAwaiter().GetResult();
        }
        catch
        {
            source.Stop();
            throw;
        }

        _source = source;
        _stopping = false;
        IsRunning = true;
        _thread = new Thread(PollLoop) { IsBackground = true, Name = "recmode-webcam-source" };
        _thread.Start();
    }

    /// <summary>How long a wait for the next camera frame blocks before re-checking <see cref="_stopping"/>.
    /// Bounded rather than infinite purely so teardown is prompt; it is not a polling interval — in steady
    /// state the wait is released by the camera's own frame event.</summary>
    private const int FrameWaitMs = 100;

    private void PollLoop()
    {
        WebcamCaptureSource source = _source!;
        byte[] bgra = []; // grown on first frame by TryGetLatestFrame, then reused — this thread owns it

        // Event-driven rather than polled (§3.9). This loop used to spin on Thread.Sleep(1)/(4) and run a
        // full BGRA→NV12 conversion every _minFrameIntervalTicks regardless of whether the camera had
        // produced anything new — at a 60 fps target against a typical 30 fps camera, half of a ~4M-operation
        // scalar conversion per frame was re-converting pixels that hadn't changed, on top of ~1000 wakeups
        // a second. Waiting on the source's own FrameArrived caps the work at the camera's real rate.
        using var newFrame = new AutoResetEvent(false);
        void OnSourceFrameArrived()
        {
            try
            {
                newFrame.Set();
            }
            catch (ObjectDisposedException)
            {
                // WinRT's MediaFrameReader.FrameArrived unsubscription (WebcamCaptureSource.StopAsync) isn't
                // guaranteed to block until an already-in-flight callback on the camera's own thread finishes
                // — that callback can still reach here and call Set() a moment after this method's `finally`
                // has unsubscribed and the `using` above has disposed newFrame. Harmless to drop: this loop
                // has already exited by the time that can happen, so there's nothing left to wake up.
            }
        }
        source.FrameArrived += OnSourceFrameArrived;
        try
        {
            while (!_stopping)
            {
                bool signaled = newFrame.WaitOne(FrameWaitMs);

                // On a timeout there is nothing new to convert — unless no frame has ever been produced, in
                // which case this also covers the narrow race where the very first frame landed between
                // StartAsync() returning and the subscription above taking effect.
                if (!signaled && _hasLatest)
                {
                    continue;
                }

                if (!source.TryGetLatestFrame(ref bgra, out int w, out int h, out int _) || w <= 0 || h <= 0)
                {
                    continue;
                }

                if (!_rateLimiter.ShouldAccept(Stopwatch.GetTimestamp()))
                {
                    continue;
                }

                // Convert into the scratch buffer, then swap references under the lock — the recording path
                // used to memcpy the whole NV12 frame here while the preview path did the cheap swap, which
                // was backwards relative to §3.9's allocation/copy-free hot-path rule.
                Bgra8ToNv12Converter.Convert(bgra, w, h, _dstW, _dstH, _scratch);
                lock (_sync)
                {
                    (_scratch, _latest) = (_latest, _scratch);
                    _hasLatest = true;
                }
                Interlocked.Increment(ref _capturedFrames);
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, ex);
        }
        finally
        {
            source.FrameArrived -= OnSourceFrameArrived;
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
            Buffer.BlockCopy(_latest, 0, dest, 0, Nv12ByteSize);
            return true;
        }
    }

    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect) { } // nonsensical when the webcam already IS the source
    public void SetBrightness(double value) { } // no GPU pass to apply it — see class doc comment
    public void SetZoomTarget(RegionRect? rect) { }
    public void SetBaseRect(RegionRect rect) { }

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
}
