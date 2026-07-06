using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Capture;

/// <summary>
/// Converts a captured BGRA texture to NV12 on the GPU in a single VideoProcessor pass (with optional
/// scaling), then reads the NV12 result back into a tightly-packed byte buffer. This is the plan's Tier-1
/// "NV12 conversion first, then readback" path (§3.3), validated in the Phase 0.5 spike.
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

    // Allocation-free steady state (plan §3.9): WGC's frame pool cycles a small fixed set of physical
    // textures for the capture session's lifetime, so the input view — keyed by the texture's native
    // pointer — is created once per distinct texture and reused forever after, instead of once per frame.
    private readonly Dictionary<IntPtr, ID3D11VideoProcessorInputView> _inputViewCache = [];
    private readonly VideoProcessorStream[] _streamBuffer = new VideoProcessorStream[1];

    public int OutputWidth { get; }
    public int OutputHeight { get; }

    /// <summary>Tightly-packed NV12 size = W*H (Y) + W*H/2 (interleaved UV).</summary>
    public int Nv12ByteSize => OutputWidth * OutputHeight * 3 / 2;

    public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context, int srcW, int srcH, int dstW, int dstH,
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
            InputFrameRate = new Rational(60, 1),
            OutputFrameRate = new Rational(60, 1),
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

        // Region capture: crop the source to the region rect; the VideoProcessor scales it to the NV12 output.
        if (sourceRect is { } r)
        {
            _videoContext.VideoProcessorSetStreamSourceRect(_processor, 0, true,
                new Vortice.RawRect(r.X, r.Y, r.X + r.Width, r.Y + r.Height));
        }
    }

    /// <summary>Converts <paramref name="src"/> to NV12 and copies tightly-packed bytes into <paramref name="dest"/>.</summary>
    public void Convert(ID3D11Texture2D src, byte[] dest)
    {
        ID3D11VideoProcessorInputView inputView = GetOrCreateInputView(src);
        _streamBuffer[0] = new VideoProcessorStream { Enable = true, InputSurface = inputView };
        _videoContext.VideoProcessorBlt(_processor, _outputView, 0, 1, _streamBuffer);

        _context.CopyResource(_nv12Staging, _nv12Gpu);
        ReadbackTightlyPacked(dest);
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
        ID3D11VideoProcessorInputView view = _videoDevice.CreateVideoProcessorInputView(src, _enumerator, inDesc);
        _inputViewCache[src.NativePointer] = view;
        return view;
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
                for (int y = 0; y < h; y++)
                {
                    Buffer.MemoryCopy(srcBase + (long)y * rowPitch, dstBase + (long)y * w, w, w);
                }

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
        foreach (ID3D11VideoProcessorInputView view in _inputViewCache.Values)
        {
            view.Dispose();
        }
        _inputViewCache.Clear();

        _outputView.Dispose();
        _nv12Staging.Dispose();
        _nv12Gpu.Dispose();
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
