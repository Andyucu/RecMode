using RecMode.Capture.Webcam;
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

    public int OutputWidth { get; }
    public int OutputHeight { get; }

    protected VideoProcessorPipeline(ID3D11Device device, ID3D11DeviceContext context, int srcW, int srcH,
        int dstW, int dstH, int frameRate, Format outputFormat, RegionRect? sourceRect)
    {
        Context = context;
        OutputWidth = dstW;
        OutputHeight = dstH;

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
        if (sourceRect is { } r)
        {
            VideoContext.VideoProcessorSetStreamSourceRect(Processor, 0, true,
                new Vortice.RawRect(r.X, r.Y, r.X + r.Width, r.Y + r.Height));
        }

        _webcamCompositor = new WebcamOverlayCompositor(device, context, VideoDevice, Enumerator);
    }

    /// <summary>Enables/disables the webcam picture-in-picture overlay; null source disables it.</summary>
    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect)
    {
        _webcamSource = source;
        _webcamRect = rect;
    }

    /// <summary>Blts <paramref name="src"/> through the VideoProcessor (compositing the webcam overlay, if
    /// set and available) and reads the result back via the subclass's format-specific readback.</summary>
    protected void BltAndReadback(ID3D11Texture2D src, byte[] dest)
    {
        ID3D11VideoProcessorInputView inputView = GetOrCreateInputView(src);

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
