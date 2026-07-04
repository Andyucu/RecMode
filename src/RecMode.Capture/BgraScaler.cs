using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Capture;

/// <summary>
/// Scales a captured BGRA texture down on the GPU (VideoProcessor) and reads it back as tightly-packed
/// BGRA for a WPF <c>WriteableBitmap</c> preview. Separate from <see cref="Nv12Converter"/> because preview
/// wants BGRA (WPF-friendly) at a small size, while recording wants full-size NV12.
/// </summary>
internal sealed class BgraScaler : IDisposable
{
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessorOutputView _outputView;
    private readonly ID3D11Texture2D _bgraGpu;
    private readonly ID3D11Texture2D _bgraStaging;

    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public int Stride => OutputWidth * 4;
    public int ByteSize => Stride * OutputHeight;

    public BgraScaler(ID3D11Device device, ID3D11DeviceContext context, int srcW, int srcH, int dstW, int dstH,
        RegionRect? sourceRect = null)
    {
        _context = context;
        OutputWidth = dstW;
        OutputHeight = dstH;

        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();

        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = (uint)srcW,
            InputHeight = (uint)srcH,
            OutputWidth = (uint)dstW,
            OutputHeight = (uint)dstH,
            InputFrameRate = new Rational(30, 1),
            OutputFrameRate = new Rational(30, 1),
            Usage = VideoUsage.PlaybackNormal,
        };
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(content);
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

        var gpuDesc = new Texture2DDescription
        {
            Width = (uint)dstW,
            Height = (uint)dstH,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _bgraGpu = device.CreateTexture2D(gpuDesc);
        _bgraStaging = device.CreateTexture2D(gpuDesc with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        });

        _outputView = _videoDevice.CreateVideoProcessorOutputView(_bgraGpu, _enumerator,
            new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });

        if (sourceRect is { } r)
        {
            _videoContext.VideoProcessorSetStreamSourceRect(_processor, 0, true,
                new Vortice.RawRect(r.X, r.Y, r.X + r.Width, r.Y + r.Height));
        }
    }

    public void Scale(ID3D11Texture2D src, byte[] dest)
    {
        var inDesc = new VideoProcessorInputViewDescription
        {
            FourCC = 0,
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
        };
        using ID3D11VideoProcessorInputView inputView = _videoDevice.CreateVideoProcessorInputView(src, _enumerator, inDesc);

        var stream = new VideoProcessorStream { Enable = true, InputSurface = inputView };
        _videoContext.VideoProcessorBlt(_processor, _outputView, 0, 1, [stream]);

        _context.CopyResource(_bgraStaging, _bgraGpu);
        Readback(dest);
    }

    private unsafe void Readback(byte[] dest)
    {
        MappedSubresource map = _context.Map(_bgraStaging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            byte* srcBase = (byte*)map.DataPointer;
            int rowPitch = (int)map.RowPitch;
            int tight = Stride;
            fixed (byte* dstBase = dest)
            {
                for (int y = 0; y < OutputHeight; y++)
                {
                    Buffer.MemoryCopy(srcBase + (long)y * rowPitch, dstBase + (long)y * tight, tight, tight);
                }
            }
        }
        finally
        {
            _context.Unmap(_bgraStaging, 0);
        }
    }

    public void Dispose()
    {
        _outputView.Dispose();
        _bgraStaging.Dispose();
        _bgraGpu.Dispose();
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
