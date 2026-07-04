using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Spike;

/// <summary>
/// Converts a captured BGRA texture to NV12 on the GPU in a single VideoProcessor pass (with optional
/// scaling), then reads the NV12 result back into a tightly-packed byte buffer for the pipe. This is the
/// plan's "NV12 conversion first, then readback" Tier-1 path (§3.3). Disposable spike code.
/// </summary>
internal sealed class Nv12Converter : IDisposable
{
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessorOutputView _outputView;
    private readonly ID3D11Texture2D _nv12Gpu;
    private readonly ID3D11Texture2D _nv12Staging;

    public int OutputWidth { get; }
    public int OutputHeight { get; }

    /// <summary>Tightly-packed NV12 size = W*H (Y) + W*H/2 (interleaved UV).</summary>
    public int Nv12ByteSize => OutputWidth * OutputHeight * 3 / 2;

    public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context, int srcW, int srcH, int dstW, int dstH)
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
            InputFrameRate = new Rational(60, 1),
            OutputFrameRate = new Rational(60, 1),
            Usage = VideoUsage.PlaybackNormal,
        };
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(content);
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

        // NV12 render target (GPU) + CPU-readable staging copy.
        var gpuDesc = new Texture2DDescription
        {
            Width = (uint)dstW,
            Height = (uint)dstH,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _nv12Gpu = device.CreateTexture2D(gpuDesc);

        var stagingDesc = gpuDesc with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        };
        _nv12Staging = device.CreateTexture2D(stagingDesc);

        var outDesc = new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D };
        _outputView = _videoDevice.CreateVideoProcessorOutputView(_nv12Gpu, _enumerator, outDesc);
    }

    /// <summary>Converts <paramref name="src"/> to NV12 and copies tightly-packed bytes into <paramref name="dest"/>.</summary>
    public void Convert(ID3D11Texture2D src, byte[] dest)
    {
        var inDesc = new VideoProcessorInputViewDescription
        {
            FourCC = 0,
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
        };
        using ID3D11VideoProcessorInputView inputView = _videoDevice.CreateVideoProcessorInputView(src, _enumerator, inDesc);

        var stream = new VideoProcessorStream
        {
            Enable = true,
            InputSurface = inputView,
        };
        _videoContext.VideoProcessorBlt(_processor, _outputView, 0, 1, [stream]);

        _context.CopyResource(_nv12Staging, _nv12Gpu);
        ReadbackTightlyPacked(dest);
    }

    private unsafe void ReadbackTightlyPacked(byte[] dest)
    {
        MappedSubresource map = _context.Map(_nv12Staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            byte* srcBase = (byte*)map.DataPointer;
            int rowPitch = (int)map.RowPitch;
            int w = OutputWidth, h = OutputHeight;

            fixed (byte* dstBase = dest)
            {
                // Y plane: h rows of w bytes.
                for (int y = 0; y < h; y++)
                {
                    Buffer.MemoryCopy(srcBase + (long)y * rowPitch, dstBase + (long)y * w, w, w);
                }

                // UV plane: starts after h rows in the mapped resource; h/2 rows of w bytes.
                byte* uvSrc = srcBase + (long)rowPitch * h;
                byte* uvDst = dstBase + (long)w * h;
                for (int y = 0; y < h / 2; y++)
                {
                    Buffer.MemoryCopy(uvSrc + (long)y * rowPitch, uvDst + (long)y * w, w, w);
                }
            }
        }
        finally
        {
            _context.Unmap(_nv12Staging, 0);
        }
    }

    public void Dispose()
    {
        _outputView.Dispose();
        _nv12Staging.Dispose();
        _nv12Gpu.Dispose();
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
