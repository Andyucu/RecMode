using RecMode.Capture.Webcam;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;

namespace RecMode.Capture;

/// <summary>
/// Default <see cref="ICaptureEngine"/>: Windows.Graphics.Capture on the discrete GPU, converting each
/// captured BGRA frame to NV12 and publishing it as "latest" for the CFR pacer to pull. Event-driven
/// (WGC <c>FrameArrived</c>), no polling (plan §3.9). Not thread-safe across Start/Stop; call from one thread.
/// </summary>
public sealed class WgcCaptureEngine : ICaptureEngine
{
    private readonly Lock _sync = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private Nv12Converter? _converter;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private DesktopDuplicationCaptureSource? _ddaSource;
    private Thread? _ddaThread;
    private volatile bool _ddaStopping;
    private byte[] _latest = [];
    private byte[] _scratch = [];
    private bool _hasLatest;
    private long _capturedFrames;
    private IWebcamFrameSource? _webcamSource;
    private RegionRect? _webcamRect;

    public bool IsRunning { get; private set; }
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }
    public int Nv12ByteSize { get; private set; }
    public long CapturedFrameCount => Interlocked.Read(ref _capturedFrames);

    public void Start(CaptureTarget target, int dstW, int dstH, bool captureCursor)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRunning)
        {
            throw new InvalidOperationException("Capture is already running.");
        }

        if (!CaptureCapabilities.IsSupported())
        {
            throw new NotSupportedException("Windows.Graphics.Capture is not supported on this OS.");
        }

        if (target.Kind == CaptureKind.AllDisplays)
        {
            StartAllDisplays(dstW, dstH);
            return;
        }

        WgcSessionFactory.Session session = WgcSessionFactory.Start(target, captureCursor, OnFrameArrived);
        _device = session.Device;
        _context = session.Context;
        _framePool = session.FramePool;
        _session = session.CaptureSession;

        int srcW = session.Item.Size.Width, srcH = session.Item.Size.Height;
        _converter = new Nv12Converter(_device, _context, srcW, srcH, dstW, dstH, target.Region);
        _converter.SetWebcamOverlay(_webcamSource, _webcamRect);
        OutputWidth = dstW;
        OutputHeight = dstH;
        Nv12ByteSize = _converter.Nv12ByteSize;
        _latest = new byte[Nv12ByteSize];
        _scratch = new byte[Nv12ByteSize];
        _hasLatest = false;
        _capturedFrames = 0;
        IsRunning = true;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object? args)
    {
        using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
        if (frame is null || _converter is null)
        {
            return;
        }

        using ID3D11Texture2D tex = CaptureInterop.GetTexture(frame.Surface);
        _converter.Convert(tex, _scratch);
        lock (_sync)
        {
            (_scratch, _latest) = (_latest, _scratch);
            _hasLatest = true;
        }

        Interlocked.Increment(ref _capturedFrames);
    }

    /// <summary>"All Displays" source: no WGC item exists for this, so <see cref="DesktopDuplicationCaptureSource"/>
    /// composites every monitor via DXGI Desktop Duplication instead, pulled from a dedicated thread (DDA has no
    /// event-driven callback like WGC's <c>FrameArrived</c> — <c>AcquireNextFrame</c> is a blocking wait, not a
    /// busy-poll, so this doesn't run against plan §3.9's "event-driven, never polled" rule).</summary>
    private void StartAllDisplays(int dstW, int dstH)
    {
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        _ddaSource = new DesktopDuplicationCaptureSource(monitors);
        _device = _ddaSource.Device;
        _context = _ddaSource.Context;

        _converter = new Nv12Converter(_device, _context, _ddaSource.VirtualWidth, _ddaSource.VirtualHeight, dstW, dstH);
        _converter.SetWebcamOverlay(_webcamSource, _webcamRect);
        OutputWidth = dstW;
        OutputHeight = dstH;
        Nv12ByteSize = _converter.Nv12ByteSize;
        _latest = new byte[Nv12ByteSize];
        _scratch = new byte[Nv12ByteSize];
        _hasLatest = false;
        _capturedFrames = 0;

        _ddaStopping = false;
        _ddaThread = new Thread(DdaPumpLoop) { IsBackground = true, Name = "recmode-dda" };
        _ddaThread.Start();
        IsRunning = true;
    }

    private void DdaPumpLoop()
    {
        while (!_ddaStopping)
        {
            ID3D11Texture2D canvas = _ddaSource!.AcquireNextFrame(timeoutMs: 16);
            _converter!.Convert(canvas, _scratch);
            lock (_sync)
            {
                (_scratch, _latest) = (_latest, _scratch);
                _hasLatest = true;
            }

            Interlocked.Increment(ref _capturedFrames);
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

    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect)
    {
        _webcamSource = source;
        _webcamRect = rect;
        _converter?.SetWebcamOverlay(source, rect);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
        }

        _ddaStopping = true;
        _ddaThread?.Join(1000);

        _session?.Dispose();
        _framePool?.Dispose();
        _ddaSource?.Dispose();
        _converter?.Dispose();
        _context?.Dispose();
        _device?.Dispose();

        _session = null;
        _framePool = null;
        _ddaSource = null;
        _ddaThread = null;
        _converter = null;
        _context = null;
        _device = null;
        _hasLatest = false;
    }

    public void Dispose() => Stop();
}
