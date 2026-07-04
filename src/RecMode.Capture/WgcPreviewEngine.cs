using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

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
    private byte[] _latest = [];
    private byte[] _scratch = [];
    private bool _hasLatest;
    private long _lastFrameTicks;

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

        (_device, _context) = CaptureInterop.CreateDevice();
        IDirect3DDevice winrt = CaptureInterop.CreateWinRtDevice(_device);
        GraphicsCaptureItem item = CaptureInterop.CreateItem(target);

        int srcW = Math.Max(2, item.Size.Width), srcH = Math.Max(2, item.Size.Height);
        // For a region, the preview should be sized to the region, not the whole monitor.
        int effectiveW = target.Region?.Width ?? srcW;
        int effectiveH = target.Region?.Height ?? srcH;
        (int dstW, int dstH) = FitPreview(Math.Max(2, effectiveW), Math.Max(2, effectiveH));
        _scaler = new BgraScaler(_device, _context, srcW, srcH, dstW, dstH, target.Region);
        Width = dstW;
        Height = dstH;
        Stride = _scaler.Stride;
        ByteSize = _scaler.ByteSize;
        _latest = new byte[ByteSize];
        _scratch = new byte[ByteSize];
        _hasLatest = false;
        _lastFrameTicks = 0;

        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(item);
        CaptureSessionConfig.Apply(_session, captureCursor);
        _session.StartCapture();
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

        _session?.Dispose();
        _framePool?.Dispose();
        _scaler?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _session = null;
        _framePool = null;
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
