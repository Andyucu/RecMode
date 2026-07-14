using RecMode.Capture.Webcam;
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
    private const int MaxPreviewWidth = 1280;
    private const int MaxPreviewHeight = 720;
    private static readonly TimeSpan MinFrameInterval = TimeSpan.FromMilliseconds(33); // ~30 fps

    private readonly Lock _sync = new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private BgraScaler? _scaler;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private DesktopDuplicationCaptureSource? _ddaSource;
    private Thread? _ddaThread;
    private volatile bool _ddaStopping;
    private byte[] _latest = [];
    private byte[] _scratch = [];
    private bool _hasLatest;
    private long _lastFrameTicks;
    private IWebcamFrameSource? _webcamSource;
    private RegionRect? _webcamRect;
    private double _brightness;

    public bool IsRunning { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Stride { get; private set; }
    public int ByteSize { get; private set; }

    public event Action? FrameAvailable;

    public void Start(CaptureTarget target, bool captureCursor)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRunning)
        {
            Stop();
        }

        if (target.Kind == CaptureKind.AllDisplays)
        {
            StartAllDisplays();
            return;
        }

        WgcSessionFactory.Session session = WgcSessionFactory.Start(target, captureCursor, OnFrameArrived);
        _device = session.Device;
        _context = session.Context;
        _framePool = session.FramePool;
        _session = session.CaptureSession;

        int srcW = Math.Max(2, session.Item.Size.Width), srcH = Math.Max(2, session.Item.Size.Height);
        // For a region, the preview should be sized to the region, not the whole monitor.
        int effectiveW = target.Region?.Width ?? srcW;
        int effectiveH = target.Region?.Height ?? srcH;
        (int dstW, int dstH) = FitPreview(Math.Max(2, effectiveW), Math.Max(2, effectiveH));
        _scaler = new BgraScaler(_device, _context, srcW, srcH, dstW, dstH, target.Region);
        _scaler.SetWebcamOverlay(_webcamSource, _webcamRect);
        _scaler.SetBrightness(_brightness);
        Width = dstW;
        Height = dstH;
        Stride = _scaler.Stride;
        ByteSize = _scaler.ByteSize;
        _latest = new byte[ByteSize];
        _scratch = new byte[ByteSize];
        _hasLatest = false;
        _lastFrameTicks = 0;
        IsRunning = true;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object? args)
    {
        using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
        if (frame is null || _scaler is null)
        {
            return;
        }

        // Throttle to ≤ 30 fps — cheap early-out before the GPU scale + readback.
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long minTicks = (long)(MinFrameInterval.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        if (_lastFrameTicks != 0 && now - _lastFrameTicks < minTicks)
        {
            return;
        }
        _lastFrameTicks = now;

        using ID3D11Texture2D tex = CaptureInterop.GetTexture(frame.Surface);
        _scaler.Scale(tex, _scratch);
        lock (_sync)
        {
            (_scratch, _latest) = (_latest, _scratch);
            _hasLatest = true;
        }

        FrameAvailable?.Invoke();
    }

    /// <summary>"All Displays" preview: same DXGI Desktop Duplication composite as <see cref="WgcCaptureEngine"/>,
    /// pulled from a dedicated thread and throttled to the same ≤ 30 fps as the WGC path.</summary>
    private void StartAllDisplays()
    {
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
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
        _lastFrameTicks = 0;

        _ddaStopping = false;
        _ddaThread = new Thread(DdaPumpLoop) { IsBackground = true, Name = "recmode-preview-dda" };
        _ddaThread.Start();
        IsRunning = true;
    }

    private void DdaPumpLoop()
    {
        long minTicks = (long)(MinFrameInterval.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        while (!_ddaStopping)
        {
            ID3D11Texture2D canvas = _ddaSource!.AcquireNextFrame(timeoutMs: 16);

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastFrameTicks != 0 && now - _lastFrameTicks < minTicks)
            {
                continue;
            }
            _lastFrameTicks = now;

            _scaler!.Scale(canvas, _scratch);
            lock (_sync)
            {
                (_scratch, _latest) = (_latest, _scratch);
                _hasLatest = true;
            }

            FrameAvailable?.Invoke();
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
        if (!IsRunning && _session is null)
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
        _scaler?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _session = null;
        _framePool = null;
        _ddaSource = null;
        _ddaThread = null;
        _scaler = null;
        _context = null;
        _device = null;
        _hasLatest = false;
    }

    public void Dispose() => Stop();

    private static (int, int) FitPreview(int srcW, int srcH)
    {
        double scale = Math.Min(1.0, Math.Min(MaxPreviewWidth / (double)srcW, MaxPreviewHeight / (double)srcH));
        int w = Math.Max(2, (int)Math.Round(srcW * scale));
        int h = Math.Max(2, (int)Math.Round(srcH * scale));
        return (w % 2 == 0 ? w : w - 1, h % 2 == 0 ? h : h - 1);
    }
}
