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
    private GraphicsCaptureItem? _item;
    private DesktopDuplicationCaptureSource? _ddaSource;
    private Thread? _ddaThread;
    private volatile bool _ddaStopping;
    private readonly ManualResetEventSlim _ddaThreadExited = new(initialState: false);
    private byte[] _latest = [];
    private byte[] _scratch = [];
    private bool _hasLatest;
    private long _capturedFrames;
    private IWebcamFrameSource? _webcamSource;
    private RegionRect? _webcamRect;
    private double _brightness;
    private GdiCaptureEngine? _softwareFallback;

    public bool IsRunning { get; private set; }
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }
    public int Nv12ByteSize { get; private set; }
    public long CapturedFrameCount => Interlocked.Read(ref _capturedFrames);
    public bool SupportsZoom => _softwareFallback is null;

    /// <summary>True once HDR-to-SDR tone mapping (§3.6) is actually active for the current recording — only
    /// ever true for Monitor/Region sources on an HDR-active display; Window and All-Displays sources don't
    /// attempt it in this first cut (documented scope cut, same precedent as Smart auto-zoom's Window/
    /// All-Displays exclusion — mapping either reliably onto one monitor's HDR state needs more plumbing than
    /// a first pass warrants).</summary>
    public bool HdrToneMapActive => _converter?.HdrToneMapActive ?? false;

    public event EventHandler<Exception>? Faulted;

    public void Start(CaptureTarget target, int dstW, int dstH, bool captureCursor)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRunning)
        {
            throw new InvalidOperationException("Capture is already running.");
        }

        if (!CaptureCapabilities.IsSupported())
        {
            StartSoftwareFallback(target, dstW, dstH, captureCursor);
            return;
        }

        if (target.Kind == CaptureKind.AllDisplays)
        {
            try { StartAllDisplays(dstW, dstH); }
            catch (Exception) { StartSoftwareFallback(target, dstW, dstH, captureCursor); }
            return;
        }

        // HDR-to-SDR tone mapping (§3.6): only attempted for Monitor/Region sources, where the target maps
        // 1:1 onto one real monitor's HDR state — Window and All-Displays sources are a deliberate scope cut
        // (same precedent as Smart auto-zoom's Window/All-Displays exclusion).
        bool sourceIsHdr = target.Kind is CaptureKind.Monitor or CaptureKind.Region &&
            CaptureCapabilities.EnumerateMonitors().FirstOrDefault(m => m.Handle == target.Handle) is { IsHdr: true };

        WgcSessionFactory.Session session;
        try { session = WgcSessionFactory.Start(target, captureCursor, OnFrameArrived, sourceIsHdr); }
        catch (Exception)
        {
            StartSoftwareFallback(target, dstW, dstH, captureCursor);
            return;
        }
        try
        {
            _device = session.Device;
            _context = session.Context;
            _framePool = session.FramePool;
            _session = session.CaptureSession;
            _item = session.Item;
            _item.Closed += OnCaptureItemClosed;

            int srcW = session.Item.Size.Width, srcH = session.Item.Size.Height;
            _converter = new Nv12Converter(_device, _context, srcW, srcH, dstW, dstH, target.Region, sourceIsHdr);
            _converter.SetWebcamOverlay(_webcamSource, _webcamRect);
            _converter.SetBrightness(_brightness);
            OutputWidth = dstW;
            OutputHeight = dstH;
            Nv12ByteSize = _converter.Nv12ByteSize;
            _latest = new byte[Nv12ByteSize];
            _scratch = new byte[Nv12ByteSize];
            _hasLatest = false;
            _capturedFrames = 0;
            IsRunning = true;
        }
        catch
        {
            DisposeWgcResources();
            throw;
        }
    }

    private void StartSoftwareFallback(CaptureTarget target, int dstW, int dstH, bool captureCursor)
    {
        _softwareFallback = new GdiCaptureEngine();
        _softwareFallback.Faulted += (s, e) => Faulted?.Invoke(this, e);
        _softwareFallback.Start(target, dstW, dstH, captureCursor);
        OutputWidth = dstW;
        OutputHeight = dstH;
        Nv12ByteSize = _softwareFallback.Nv12ByteSize;
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

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object? args)
    {
        Faulted?.Invoke(this, new InvalidOperationException("The captured window or display was closed."));
        ThreadPool.QueueUserWorkItem(_ => Stop());
    }

    private void DisposeWgcResources()
    {
        if (_framePool is not null) _framePool.FrameArrived -= OnFrameArrived;
        if (_item is not null) _item.Closed -= OnCaptureItemClosed;
        _session?.Dispose(); _framePool?.Dispose(); _converter?.Dispose(); _context?.Dispose(); _device?.Dispose();
        _session = null; _framePool = null; _item = null; _converter = null; _context = null; _device = null;
    }

    /// <summary>"All Displays" source: no WGC item exists for this, so <see cref="DesktopDuplicationCaptureSource"/>
    /// composites every monitor via DXGI Desktop Duplication instead, pulled from a dedicated thread (DDA has no
    /// event-driven callback like WGC's <c>FrameArrived</c> — <c>AcquireNextFrame</c> is a blocking wait, not a
    /// busy-poll, so this doesn't run against plan §3.9's "event-driven, never polled" rule).</summary>
    private void StartAllDisplays(int dstW, int dstH)
    {
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        try
        {
            _ddaSource = new DesktopDuplicationCaptureSource(monitors);
            _device = _ddaSource.Device;
            _context = _ddaSource.Context;
            _converter = new Nv12Converter(_device, _context, _ddaSource.VirtualWidth, _ddaSource.VirtualHeight, dstW, dstH);
            _converter.SetWebcamOverlay(_webcamSource, _webcamRect);
            _converter.SetBrightness(_brightness);
            OutputWidth = dstW; OutputHeight = dstH; Nv12ByteSize = _converter.Nv12ByteSize;
            _latest = new byte[Nv12ByteSize]; _scratch = new byte[Nv12ByteSize]; _hasLatest = false; _capturedFrames = 0;
            _ddaStopping = false; _ddaThreadExited.Reset();
            _ddaThread = new Thread(DdaPumpLoop) { IsBackground = true, Name = "recmode-dda" };
            _ddaThread.Start(); IsRunning = true;
        }
        catch
        {
            _converter?.Dispose(); _ddaSource?.Dispose(); _context?.Dispose(); _device?.Dispose();
            _converter = null; _ddaSource = null; _context = null; _device = null;
            throw;
        }
    }

    /// <summary>
    /// Runs on its own thread for the lifetime of the "All Displays" source. Owns <see cref="_ddaSource"/>
    /// and <see cref="_converter"/> exclusively while running: no other thread touches them, and (critically)
    /// only this thread disposes them, in its own <c>finally</c>, once it has actually stopped using them.
    /// <see cref="Stop"/> only signals <see cref="_ddaStopping"/> and waits — it must never dispose these out
    /// from under a thread that might still be inside <c>AcquireNextFrame</c>/<c>Convert</c>. Any exception
    /// (a GPU hiccup, a dead output, etc.) is caught here so it degrades capture instead of taking the whole
    /// process down, since unhandled exceptions on a non-UI thread terminate the app by default.
    /// </summary>
    private void DdaPumpLoop()
    {
        DesktopDuplicationCaptureSource? ddaSource = _ddaSource;
        Nv12Converter? converter = _converter;
        ID3D11DeviceContext? context = _context;
        ID3D11Device? device = _device;
        try
        {
            while (!_ddaStopping)
            {
                ID3D11Texture2D canvas = ddaSource!.AcquireNextFrame(timeoutMs: 16);
                converter!.Convert(canvas, _scratch);
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
            try
            {
                Faulted?.Invoke(this, ex);
            }
            catch (Exception)
            {
                // A misbehaving subscriber must not prevent this thread from shutting down cleanly.
            }
        }
        finally
        {
            // Device/context for the DDA path are created by _ddaSource and used only by this thread
            // (via AcquireNextFrame/Convert) — dispose them here too, alongside the source and converter,
            // rather than in Stop(), for the same "only the last user disposes" reasoning.
            ddaSource?.Dispose();
            converter?.Dispose();
            context?.Dispose();
            device?.Dispose();
            _ddaThreadExited.Set();
        }
    }

    public bool TryGetLatestFrame(byte[] dest)
    {
        if (_softwareFallback is not null)
            return _softwareFallback.TryGetLatestFrame(dest);
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
        if (_softwareFallback is not null) return;
        _webcamSource = source;
        _webcamRect = rect;
        _converter?.SetWebcamOverlay(source, rect);
    }

    public void SetBrightness(double value)
    {
        if (_softwareFallback is not null) return;
        _brightness = value;
        _converter?.SetBrightness(value);
    }

    public void SetZoomTarget(RegionRect? rect)
    {
        if (_softwareFallback is not null) return;
        _converter?.SetZoomTarget(rect);
    }

    public void SetBaseRect(RegionRect rect)
    {
        if (_softwareFallback is not null) return;
        _converter?.SetBaseRect(rect);
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        if (_softwareFallback is not null)
        {
            _softwareFallback.Dispose();
            _softwareFallback = null;
            _hasLatest = false;
            return;
        }

        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
        }

        bool wasDda = _ddaThread is not null;
        if (wasDda)
        {
            // Signal and wait for confirmation the thread actually stopped — never dispose _ddaSource,
            // _converter, _context, or _device out from under it. If it doesn't exit in time (a stuck
            // AcquireNextFrame/GPU call), those resources are deliberately left alive: the thread's own
            // finally block (DdaPumpLoop) disposes them itself whenever it does eventually exit, and this
            // instance just drops its references below instead of double-disposing.
            _ddaStopping = true;
            bool exited = _ddaThreadExited.Wait(TimeSpan.FromSeconds(5));
            if (!exited)
            {
                Faulted?.Invoke(this, new TimeoutException(
                    "The desktop-duplication capture thread did not stop within 5 seconds; its resources will be released once it does."));
            }
        }

        if (!wasDda)
        {
            DisposeWgcResources();
        }

        _session = null;
        _item = null;
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
