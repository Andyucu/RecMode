using RecMode.Capture.Webcam;
using Serilog;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;

namespace RecMode.Capture;

/// <summary>
/// Default <see cref="IPreviewEngine"/>: a WGC session scaled to a small BGRA image, throttled to ≤ 30 fps.
/// Independent of the recording engine (preview and recording use separate sessions and don't run at once —
/// preview pauses while recording, plan §3.9).
/// </summary>
public sealed class WgcPreviewEngine : IPreviewEngine
{
    private const int TargetFps = 30;

    private int _maxPreviewWidth = 1280;
    private int _maxPreviewHeight = 720;

    // Guards against re-entrant Stop() calls: OnCaptureItemClosed queues Stop() on the thread pool, which can
    // race a user-initiated Stop()/Start() (e.g. closing the previewed window right as the user navigates
    // away). Without this, both could observe IsRunning == true and both run the teardown below, double-
    // releasing the same D3D11 COM objects — an access violation that kills the process. Identical fix to
    // the one WgcCaptureEngine.Stop() already has, for the same reason.
    private readonly Lock _stopLock = new();
    private readonly Lock _sync = new();
    // Held for the whole duration of OnFrameArrived's GPU work, and taken (empty critical section) by Stop()
    // after unsubscribing but before disposing the scaler/context/device — see WgcCaptureEngine, which has
    // the identical callback shape and the same barrier for the same reason. Without it, a FrameArrived
    // callback already dispatched when Stop() unsubscribes can be mid-Scale() (a VideoProcessorBlt +
    // staging readback, several ms) while those COM objects are released underneath it: an access violation,
    // which is not catchable and terminates the process. Deliberately a separate lock from _sync, so the
    // multi-ms GPU work never contends with TryGetLatestFrame's plain memcpy.
    private readonly Lock _disposeGuard = new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private BgraScaler? _scaler;
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
    private readonly FrameRateLimiter _rateLimiter = new(System.Diagnostics.Stopwatch.Frequency);
    private IWebcamFrameSource? _webcamSource;
    private RegionRect? _webcamRect;
    private double _brightness;

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

        _maxPreviewWidth = Math.Max(2, maxWidth);
        _maxPreviewHeight = Math.Max(2, maxHeight);

        if (target.Kind == CaptureKind.AllDisplays)
        {
            StartAllDisplays();
            return;
        }

        WgcSessionFactory.Session session = WgcSessionFactory.Start(target, captureCursor, OnFrameArrived);
        try
        {
            _device = session.Device; _context = session.Context; _framePool = session.FramePool; _session = session.CaptureSession;
            _item = session.Item; _item.Closed += OnCaptureItemClosed;
            int srcW = Math.Max(2, session.Item.Size.Width), srcH = Math.Max(2, session.Item.Size.Height);
            int effectiveW = target.Region?.Width ?? srcW, effectiveH = target.Region?.Height ?? srcH;
            (int dstW, int dstH) = FitPreview(Math.Max(2, effectiveW), Math.Max(2, effectiveH));
            _scaler = new BgraScaler(_device, _context, srcW, srcH, dstW, dstH, target.Region);
            _scaler.SetWebcamOverlay(_webcamSource, _webcamRect); _scaler.SetBrightness(_brightness);
            Width = dstW; Height = dstH; Stride = _scaler.Stride; ByteSize = _scaler.ByteSize;
            _latest = new byte[ByteSize]; _scratch = new byte[ByteSize]; _hasLatest = false; IsRunning = true;
            _rateLimiter.SetTargetFps(TargetFps);
        }
        catch
        {
            if (_framePool is not null) _framePool.FrameArrived -= OnFrameArrived;
            if (_item is not null) _item.Closed -= OnCaptureItemClosed;
            _session?.Dispose(); _framePool?.Dispose(); _scaler?.Dispose(); _context?.Dispose(); _device?.Dispose();
            _session = null; _framePool = null; _item = null; _scaler = null; _context = null; _device = null;
            throw;
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object? args)
    {
        // All GPU work runs under _disposeGuard so Stop() can't release the scaler/context/device mid-Scale().
        // Re-reading _scaler into a local inside the lock is what makes the null check load-bearing: checking
        // the field and then dereferencing it later would still be a check-then-use race.
        lock (_disposeGuard)
        {
            using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
            BgraScaler? scaler = _scaler;
            if (frame is null || scaler is null)
            {
                return;
            }

            // Throttle to ≤ 30 fps — cheap early-out before the GPU scale + readback.
            if (!_rateLimiter.ShouldAccept(System.Diagnostics.Stopwatch.GetTimestamp()))
            {
                return;
            }

            using ID3D11Texture2D tex = CaptureInterop.GetTexture(frame.Surface);
            scaler.Scale(tex, _scratch);
            lock (_sync)
            {
                (_scratch, _latest) = (_latest, _scratch);
                _hasLatest = true;
            }
        }

        FrameAvailable?.Invoke();
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object? args) =>
        ThreadPool.QueueUserWorkItem(_ => Stop());

    /// <summary>"All Displays" preview: same DXGI Desktop Duplication composite as <see cref="WgcCaptureEngine"/>,
    /// pulled from a dedicated thread and throttled to the same ≤ 30 fps as the WGC path.</summary>
    private void StartAllDisplays()
    {
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        try
        {
        _ddaSource = new DesktopDuplicationCaptureSource(monitors);
        _device = _ddaSource.Device;
        _context = _ddaSource.Context;

        (int dstW, int dstH) = FitPreview(Math.Max(2, _ddaSource.VirtualWidth), Math.Max(2, _ddaSource.VirtualHeight));
        _scaler = new BgraScaler(_device, _context, _ddaSource.VirtualWidth, _ddaSource.VirtualHeight, dstW, dstH);
        _scaler.SetWebcamOverlay(_webcamSource, _webcamRect);
        _scaler.SetBrightness(_brightness);
        Width = dstW;
        Height = dstH;
        Stride = _scaler.Stride;
        ByteSize = _scaler.ByteSize;
        _latest = new byte[ByteSize];
        _scratch = new byte[ByteSize];
        _hasLatest = false;
        _rateLimiter.SetTargetFps(TargetFps);

        _ddaStopping = false;
        _ddaThreadExited.Reset();
        _ddaThread = new Thread(DdaPumpLoop) { IsBackground = true, Name = "recmode-preview-dda" };
        _ddaThread.Start();
        IsRunning = true;
        }
        catch
        {
            _scaler?.Dispose(); _ddaSource?.Dispose(); _context?.Dispose(); _device?.Dispose();
            _scaler = null; _ddaSource = null; _context = null; _device = null;
            throw;
        }
    }

    private void DdaPumpLoop()
    {
        DesktopDuplicationCaptureSource? ddaSource = _ddaSource;
        BgraScaler? scaler = _scaler;
        ID3D11DeviceContext? context = _context;
        ID3D11Device? device = _device;
        try
        {
            while (!_ddaStopping)
            {
                ID3D11Texture2D canvas = ddaSource!.AcquireNextFrame(timeoutMs: 16);

                if (!_rateLimiter.ShouldAccept(System.Diagnostics.Stopwatch.GetTimestamp())) continue;

                scaler!.Scale(canvas, _scratch);
                lock (_sync) { (_scratch, _latest) = (_latest, _scratch); _hasLatest = true; }
                FrameAvailable?.Invoke();
            }
        }
        catch
        {
            // The preview deliberately fails closed; recording will independently select its fallback.
        }
        finally
        {
            ddaSource?.Dispose(); scaler?.Dispose(); context?.Dispose(); device?.Dispose();
            _ddaThreadExited.Set();
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

    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect)
    {
        _webcamSource = source;
        _webcamRect = rect;
        _scaler?.SetWebcamOverlay(source, rect);
    }

    public void SetBrightness(double value)
    {
        _brightness = value;
        _scaler?.SetBrightness(value);
    }

    public void Stop()
    {
        lock (_stopLock)
        {
            if (!IsRunning && _session is null)
            {
                return;
            }

            IsRunning = false;
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }

            // Barrier against an OnFrameArrived call already dispatched when the unsubscribe above took effect —
            // it blocks here until that callback finishes its GPU work, so the disposals below can't pull the
            // scaler/context/device out from under it. Same mechanism as WgcCaptureEngine.Stop().
            lock (_disposeGuard) { }

            _ddaStopping = true;
            bool wasDda = _ddaThread is not null;
            if (wasDda && !_ddaThreadExited.Wait(TimeSpan.FromSeconds(5)))
            {
                // The DDA thread disposes its own resources in its finally block whenever it does exit, so
                // leaving them alive here is the safe choice — but it used to be silent, which made a stuck
                // duplication thread undiagnosable. (WgcCaptureEngine raises Faulted for the same case; preview
                // has no error channel of its own, so log instead.)
                Log.Warning("The preview desktop-duplication thread did not stop within 5 seconds; its resources will be released once it does");
            }

            _session?.Dispose();
            _framePool?.Dispose();
            if (!wasDda)
            {
                _ddaSource?.Dispose(); _scaler?.Dispose(); _context?.Dispose(); _device?.Dispose();
            }
            if (_item is not null) _item.Closed -= OnCaptureItemClosed;
            _session = null;
            _item = null;
            _framePool = null;
            _ddaSource = null;
            _ddaThread = null;
            _scaler = null;
            _context = null;
            _device = null;
            _hasLatest = false;
        }
    }

    public void Dispose() => Stop();

    private (int, int) FitPreview(int srcW, int srcH) =>
        PreviewSizing.Fit(srcW, srcH, _maxPreviewWidth, _maxPreviewHeight);
}
