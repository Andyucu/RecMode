using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Capture;

/// <summary>
/// Scales a captured BGRA texture down on the GPU (VideoProcessor) and reads it back as tightly-packed
/// BGRA for a WPF <c>WriteableBitmap</c> preview. Separate from <see cref="Nv12Converter"/> because preview
/// wants BGRA (WPF-friendly) at a small size, while recording wants full-size NV12 — but both share the
/// device setup, input-view cache, and webcam-overlay Blt path via <see cref="VideoProcessorPipeline"/>;
/// only the output format/frame rate and the single-plane BGRA readback are specific to this class.
/// </summary>
internal sealed class BgraScaler : VideoProcessorPipeline
{
    public int Stride => OutputWidth * 4;
    public int ByteSize => Stride * OutputHeight;

    public BgraScaler(ID3D11Device device, ID3D11DeviceContext context, int srcW, int srcH, int dstW, int dstH,
        RegionRect? sourceRect = null)
        : base(device, context, srcW, srcH, dstW, dstH, frameRate: 30, Format.B8G8R8A8_UNorm, sourceRect)
    {
    }

    public void Scale(ID3D11Texture2D src, byte[] dest) => BltAndReadback(src, dest);

    protected override unsafe void ReadbackTightlyPacked(byte[] dest)
    {
        MappedSubresource map = Context.Map(StagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
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
            Context.Unmap(StagingTexture, 0);
        }
    }
}
