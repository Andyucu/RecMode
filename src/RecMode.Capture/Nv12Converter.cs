using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Capture;

/// <summary>
/// Converts a captured BGRA texture to NV12 on the GPU in a single VideoProcessor pass (with optional
/// scaling), then reads the NV12 result back into a tightly-packed byte buffer. This is the plan's Tier-1
/// "NV12 conversion first, then readback" path (§3.3), validated in the Phase 0.5 spike. Device setup,
/// the input-view cache, and the webcam-overlay Blt path live in <see cref="VideoProcessorPipeline"/>,
/// shared with <see cref="BgraScaler"/> (preview) — only the output format/frame rate and the NV12
/// two-plane readback are specific to this class.
/// </summary>
internal sealed class Nv12Converter : VideoProcessorPipeline
{
    public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context, int srcW, int srcH, int dstW, int dstH,
        RegionRect? sourceRect = null)
        : base(device, context, srcW, srcH, dstW, dstH, frameRate: 60, Format.NV12, sourceRect)
    {
    }

    /// <summary>Tightly-packed NV12 size = W*H (Y) + W*H/2 (interleaved UV).</summary>
    public int Nv12ByteSize => OutputWidth * OutputHeight * 3 / 2;

    /// <summary>Converts <paramref name="src"/> to NV12 and copies tightly-packed bytes into <paramref name="dest"/>.</summary>
    public void Convert(ID3D11Texture2D src, byte[] dest) => BltAndReadback(src, dest);

    protected override unsafe void ReadbackTightlyPacked(byte[] dest)
    {
        MappedSubresource map = Context.Map(StagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
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
            Context.Unmap(StagingTexture, 0);
        }
    }
}
