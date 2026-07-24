using System.Diagnostics;
using RecMode.Capture.Webcam;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Capture;

/// <summary>
/// Shared GPU pipeline behind <see cref="Nv12Converter"/> (recording, NV12) and <see cref="BgraScaler"/>
/// (preview, BGRA) — device/VideoProcessor setup, the allocation-free input-view cache (§3.9: WGC's frame
/// pool cycles a small fixed set of physical textures for a session's lifetime, so the input view — keyed
/// by the texture's native pointer — is created once per distinct texture and reused forever after, instead
/// of once per frame), the webcam picture-in-picture Blt path, and teardown are identical between the two;
/// only the output pixel format/frame rate and the readback (NV12 two-plane vs BGRA single-plane) differ.
/// Factored out after the same per-frame-allocation bug had to be found and fixed independently in both
/// classes at once (2026-07-06) — a second GPU-pipeline fix applied to only one of two near-duplicate files
/// was exactly the failure mode this consolidation closes off.
/// </summary>
internal abstract class VideoProcessorPipeline : IDisposable
{
    protected readonly ID3D11DeviceContext Context;
    protected readonly ID3D11VideoDevice VideoDevice;
    protected readonly ID3D11VideoContext VideoContext;
    protected readonly ID3D11VideoProcessor Processor;
    protected readonly ID3D11VideoProcessorEnumerator Enumerator;
    protected readonly ID3D11VideoProcessorOutputView OutputView;
    protected readonly ID3D11Texture2D GpuTexture;
    protected readonly ID3D11Texture2D StagingTexture;

    private readonly Dictionary<IntPtr, ID3D11VideoProcessorInputView> _inputViewCache = [];
    private readonly VideoProcessorStream[] _streamBuffer = new VideoProcessorStream[1];
    private readonly VideoProcessorStream[] _streamBufferWithWebcam = new VideoProcessorStream[2];
    private readonly WebcamOverlayCompositor _webcamCompositor;
    private IWebcamFrameSource? _webcamSource;
    private RegionRect? _webcamRect;

    // Brightness (§ brightness slider): the device's actual filter range varies (driver-dependent), so the
    // user-facing -100..100 value is mapped into it via VideoProcessorFilterRange.Multiplier at apply time
    // rather than assumed to match 1:1. Queried once at construction; unsupported hardware just no-ops.
    private readonly bool _brightnessSupported;
    private readonly VideoProcessorFilterRange _brightnessRange;
    private double _brightnessValue;

    // Smart auto-zoom: the un-zoomed "rest" rect (the region crop, if any, else the whole source), and the
    // currently-animating pan/zoom state. Applied every frame in BltAndReadback (like brightness) rather than
    // once at construction, so a zoom target set mid-recording takes effect smoothly. Guarded by _zoomLock
    // since SetZoomTarget can be called from a different thread (the click hook) than BltAndReadback (the
    // capture callback thread) — the lock only ever guards cheap arithmetic, never a D3D11 call, so
    // BltAndReadback's actual VideoProcessorSetStreamSourceRect call always happens on its own thread.
    private static readonly double ZoomDurationSeconds = 0.45;
    private readonly int _srcW, _srcH;
    private RegionRect _restRect;
    private readonly object _zoomLock = new();
    private RegionRect _zoomFrom;
    private RegionRect _zoomTo;
    private long _zoomStartTimestamp;

    public int OutputWidth { get; }
    public int OutputHeight { get; }

    /// <summary>True once HDR-to-SDR tone mapping (§3.6) is actually configured on the VideoProcessor — only
    /// meaningful when the pipeline was constructed with <c>sourceIsHdr: true</c>; false if the driver doesn't
    /// expose <c>ID3D11VideoContext1</c> (best-effort, same "fails closed" pattern as the brightness filter).</summary>
    public bool HdrToneMapActive { get; }

    protected VideoProcessorPipeline(ID3D11Device device, ID3D11DeviceContext context, int srcW, int srcH,
        int dstW, int dstH, int frameRate, Format outputFormat, RegionRect? sourceRect, bool sourceIsHdr = false)
    {
        Context = context;
        OutputWidth = dstW;
        OutputHeight = dstH;
        _srcW = srcW;
        _srcH = srcH;

        VideoDevice = device.QueryInterface<ID3D11VideoDevice>();
        VideoContext = context.QueryInterface<ID3D11VideoContext>();

        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = (uint)srcW,
            InputHeight = (uint)srcH,
            OutputWidth = (uint)dstW,
            OutputHeight = (uint)dstH,
            InputFrameRate = new Rational((uint)frameRate, 1),
            OutputFrameRate = new Rational((uint)frameRate, 1),
            Usage = VideoUsage.PlaybackNormal,
        };
        Enumerator = VideoDevice.CreateVideoProcessorEnumerator(content);
        Processor = VideoDevice.CreateVideoProcessor(Enumerator, 0);

        var gpuDesc = new Texture2DDescription
        {
            Width = (uint)dstW,
            Height = (uint)dstH,
            MipLevels = 1,
            ArraySize = 1,
            Format = outputFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        GpuTexture = device.CreateTexture2D(gpuDesc);
        StagingTexture = device.CreateTexture2D(gpuDesc with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        });

        OutputView = VideoDevice.CreateVideoProcessorOutputView(GpuTexture, Enumerator,
            new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });

        // Region capture: crop the source to the region rect; the VideoProcessor scales it to the output.
        // The actual VideoProcessorSetStreamSourceRect call happens every frame in BltAndReadback (see
        // ApplyZoomRect) rather than once here, so smart auto-zoom can animate within this rect later.
        _restRect = sourceRect ?? new RegionRect(0, 0, srcW, srcH);
        _zoomFrom = _restRect;
        _zoomTo = _restRect;
        _zoomStartTimestamp = Stopwatch.GetTimestamp();

        _webcamCompositor = new WebcamOverlayCompositor(device, context, VideoDevice, Enumerator);

        Result filterHr = Enumerator.GetVideoProcessorFilterRange(VideoProcessorFilter.Brightness, out _brightnessRange);
        _brightnessSupported = filterHr.Success;

        HdrToneMapActive = sourceIsHdr && TryApplyHdrToneMap();
    }

    /// <summary>§3.6 HDR-to-SDR tone mapping: the source texture (only ever requested in FP16 when the source
    /// monitor is HDR — see <c>WgcSessionFactory</c>) actually holds linear scRGB values (BT.709 primaries,
    /// gamma 1.0, extended range above 1.0 for HDR highlights — this is what Windows.Graphics.Capture/DWM's own
    /// HDR compositing surface always is, regardless of the physical display's transport format). Telling the
    /// VideoProcessor the true input/output color spaces via <c>ID3D11VideoContext1</c> lets its Blt do the
    /// actual linear→gamma tone-map/clamp down to standard SDR itself, once, rather than us reinterpreting
    /// already-mangled 8-bit values after the fact (which is what capturing HDR content as plain BGRA8 — the
    /// pre-existing default — does, and is exactly what produces a washed-out/wrong-color recording). One-time
    /// setup (color space doesn't change frame to frame, unlike brightness/zoom). Best-effort: an older driver
    /// without <c>ID3D11VideoContext1</c> just leaves color space unset (fails closed, matches the brightness
    /// filter's pattern) rather than blocking capture.</summary>
    private bool TryApplyHdrToneMap()
    {
        try
        {
            using ID3D11VideoContext1 videoContext1 = VideoContext.QueryInterface<ID3D11VideoContext1>();
            videoContext1.VideoProcessorSetStreamColorSpace1(Processor, 0, ColorSpaceType.RgbFullG10NoneP709);
            videoContext1.VideoProcessorSetOutputColorSpace1(Processor, ColorSpaceType.RgbFullG22NoneP709);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Enables/disables the webcam picture-in-picture overlay; null source disables it.</summary>
    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect)
    {
        _webcamSource = source;
        _webcamRect = rect;
    }

    /// <summary>Sets the captured-video brightness adjustment, -100 (darkest) .. 100 (brightest), 0 = unchanged.
    /// No-ops on hardware whose VideoProcessor doesn't expose a Brightness filter (fails closed, not an error).</summary>
    public void SetBrightness(double value) => _brightnessValue = Math.Clamp(value, -100, 100);

    /// <summary>Smart auto-zoom: animates the GPU crop toward <paramref name="rect"/> (source-local pixels,
    /// clamped by the caller to the capture's rest rect), or back out to the full un-zoomed rest rect when
    /// <paramref name="rect"/> is null. Safe to call from any thread; only cheap arithmetic happens under the
    /// lock, never a D3D11 call.</summary>
    public void SetZoomTarget(RegionRect? rect)
    {
        lock (_zoomLock)
        {
            _zoomFrom = CurrentZoomRectLocked();
            _zoomTo = rect ?? _restRect;
            _zoomStartTimestamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Live-retargets the actual captured area — e.g. dragging the on-screen contour to a new spot
    /// while a Region-source recording is in progress — rather than a temporary pan/zoom effect layered on top
    /// of a fixed base. Takes effect immediately (no easing, unlike <see cref="SetZoomTarget"/>): a drag needs
    /// 1:1 responsiveness, not a smoothed animation lagging behind the cursor. Becomes the new "rest" position,
    /// so a subsequent auto/manual zoom eases relative to this rect, not the recording's original one.
    /// <paramref name="rect"/> is clamped to the actual source texture bounds.</summary>
    public void SetBaseRect(RegionRect rect)
    {
        int w = Math.Clamp(rect.Width, 1, _srcW);
        int h = Math.Clamp(rect.Height, 1, _srcH);
        int x = Math.Clamp(rect.X, 0, _srcW - w);
        int y = Math.Clamp(rect.Y, 0, _srcH - h);
        var clamped = new RegionRect(x, y, w, h);

        lock (_zoomLock)
        {
            _restRect = clamped;
            _zoomFrom = clamped;
            _zoomTo = clamped;
            _zoomStartTimestamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Cubic-ease-out interpolation between the last two zoom targets, evaluated at the current time.
    /// Must be called with <see cref="_zoomLock"/> held.</summary>
    private RegionRect CurrentZoomRectLocked()
    {
        double elapsed = (Stopwatch.GetTimestamp() - _zoomStartTimestamp) / (double)Stopwatch.Frequency;
        double t = Math.Clamp(elapsed / ZoomDurationSeconds, 0, 1);
        t = 1 - Math.Pow(1 - t, 3);
        return new RegionRect(
            (int)Math.Round(_zoomFrom.X + (_zoomTo.X - _zoomFrom.X) * t),
            (int)Math.Round(_zoomFrom.Y + (_zoomTo.Y - _zoomFrom.Y) * t),
            (int)Math.Round(_zoomFrom.Width + (_zoomTo.Width - _zoomFrom.Width) * t),
            (int)Math.Round(_zoomFrom.Height + (_zoomTo.Height - _zoomFrom.Height) * t));
    }

    /// <summary>Applies the currently-interpolated zoom rect as this frame's stream-0 source rect. Cheap
    /// enough to call every frame (same pattern as <see cref="ApplyBrightnessFilter"/>); no allocation.</summary>
    private void ApplyZoomRect()
    {
        RegionRect rect;
        lock (_zoomLock)
        {
            rect = CurrentZoomRectLocked();
        }

        VideoContext.VideoProcessorSetStreamSourceRect(Processor, 0, true,
            new Vortice.RawRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height));
    }

    /// <summary>Blts <paramref name="src"/> through the VideoProcessor (compositing the webcam overlay, if
    /// set and available) and reads the result back via the subclass's format-specific readback.</summary>
    protected void BltAndReadback(ID3D11Texture2D src, byte[] dest)
    {
        ID3D11VideoProcessorInputView inputView = GetOrCreateInputView(src);
        ApplyBrightnessFilter();
        ApplyZoomRect();

        if (_webcamSource is not null && _webcamRect is { } rect)
        {
            ID3D11VideoProcessorInputView? webcamView = _webcamCompositor.Update(_webcamSource);
            if (webcamView is not null)
            {
                _streamBufferWithWebcam[0] = new VideoProcessorStream { Enable = true, InputSurface = inputView };
                _streamBufferWithWebcam[1] = new VideoProcessorStream { Enable = true, InputSurface = webcamView };
                VideoContext.VideoProcessorSetStreamDestRect(Processor, 1, true,
                    new Vortice.RawRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height));
                VideoContext.VideoProcessorBlt(Processor, OutputView, 0, 2, _streamBufferWithWebcam);

                Context.CopyResource(StagingTexture, GpuTexture);
                ReadbackTightlyPacked(dest);
                return;
            }
        }

        _streamBuffer[0] = new VideoProcessorStream { Enable = true, InputSurface = inputView };
        VideoContext.VideoProcessorBlt(Processor, OutputView, 0, 1, _streamBuffer);

        Context.CopyResource(StagingTexture, GpuTexture);
        ReadbackTightlyPacked(dest);
    }

    /// <summary>Maps <see cref="StagingTexture"/> and copies it into <paramref name="dest"/> in the
    /// subclass's tightly-packed format (NV12 two-plane vs BGRA single-plane).</summary>
    protected abstract void ReadbackTightlyPacked(byte[] dest);

    /// <summary>Applies the current brightness value to stream 0 (the captured content only — never the
    /// webcam overlay, which keeps its own natural exposure). Cheap enough to call every frame; no allocation.</summary>
    private void ApplyBrightnessFilter()
    {
        if (!_brightnessSupported)
        {
            return;
        }

        bool enable = Math.Abs(_brightnessValue) > 0.01;
        int level = _brightnessRange.Default;
        if (enable)
        {
            double t = (_brightnessValue + 100) / 200; // 0..1 across the UI's -100..100 range
            level = (int)Math.Round(_brightnessRange.Minimum + t * (_brightnessRange.Maximum - _brightnessRange.Minimum));
        }

        VideoContext.VideoProcessorSetStreamFilter(Processor, 0, VideoProcessorFilter.Brightness, enable, level);
    }

    private ID3D11VideoProcessorInputView GetOrCreateInputView(ID3D11Texture2D src)
    {
        if (_inputViewCache.TryGetValue(src.NativePointer, out ID3D11VideoProcessorInputView? cached))
        {
            return cached;
        }

        var inDesc = new VideoProcessorInputViewDescription
        {
            FourCC = 0,
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
        };
        ID3D11VideoProcessorInputView view = VideoDevice.CreateVideoProcessorInputView(src, Enumerator, inDesc);
        _inputViewCache[src.NativePointer] = view;
        return view;
    }

    public virtual void Dispose()
    {
        foreach (ID3D11VideoProcessorInputView view in _inputViewCache.Values)
        {
            view.Dispose();
        }
        _inputViewCache.Clear();
        _webcamCompositor.Dispose();

        OutputView.Dispose();
        StagingTexture.Dispose();
        GpuTexture.Dispose();
        Processor.Dispose();
        Enumerator.Dispose();
        VideoContext.Dispose();
        VideoDevice.Dispose();
    }
}
