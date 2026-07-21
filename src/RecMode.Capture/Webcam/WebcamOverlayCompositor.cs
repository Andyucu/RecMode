using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Capture.Webcam;

/// <summary>
/// Uploads the latest webcam frame into a GPU texture and hands back a
/// <see cref="ID3D11VideoProcessorInputView"/> for the caller to composite as a second VideoProcessor
/// stream. Shared by <see cref="Nv12Converter"/> and <see cref="BgraScaler"/> so both the recording and
/// preview paths draw the picture-in-picture identically. The texture is (re)created lazily — nothing is
/// allocated until a real frame arrives, and it's only recreated if the camera's frame size changes.
/// </summary>
internal sealed class WebcamOverlayCompositor : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;

    private ID3D11Texture2D? _texture;
    private ID3D11VideoProcessorInputView? _inputView;
    private int _texWidth;
    private int _texHeight;

    public WebcamOverlayCompositor(ID3D11Device device, ID3D11DeviceContext context,
        ID3D11VideoDevice videoDevice, ID3D11VideoProcessorEnumerator enumerator)
    {
        _device = device;
        _context = context;
        _videoDevice = videoDevice;
        _enumerator = enumerator;
    }

    /// <summary>Uploads the source's latest frame and returns the input view to composite, or null if no frame is available yet.</summary>
    public ID3D11VideoProcessorInputView? Update(IWebcamFrameSource source)
    {
        if (!source.TryGetLatestFrame(out byte[] data, out int width, out int height, out int stride) || width <= 0 || height <= 0)
        {
            return null;
        }

        EnsureTexture(width, height);
        UploadFrame(data, stride);
        return _inputView;
    }

    private void EnsureTexture(int width, int height)
    {
        if (_texture is not null && _texWidth == width && _texHeight == height)
        {
            return;
        }

        _inputView?.Dispose();
        _texture?.Dispose();

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _texture = _device.CreateTexture2D(desc);

        var inDesc = new VideoProcessorInputViewDescription
        {
            FourCC = 0,
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
        };
        _inputView = _videoDevice.CreateVideoProcessorInputView(_texture, _enumerator, inDesc);

        _texWidth = width;
        _texHeight = height;
    }

    private unsafe void UploadFrame(byte[] data, int stride)
    {
        fixed (byte* p = data)
        {
            _context.UpdateSubresource(_texture!, 0u, null, (IntPtr)p, (uint)stride, 0u);
        }
    }

    public void Dispose()
    {
        _inputView?.Dispose();
        _texture?.Dispose();
    }
}
