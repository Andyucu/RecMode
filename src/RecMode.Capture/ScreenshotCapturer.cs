using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace RecMode.Capture;

/// <summary>A captured still frame as tightly-packed BGRA (Bgra32) for PNG encoding / clipboard.</summary>
public sealed record ScreenshotImage(int Width, int Height, int Stride, byte[] Bgra);

/// <summary>
/// One-shot full-resolution screenshot via WGC (plan Phase 5). Grabs a single BGRA frame from a target,
/// reads it back at native size (no scaling), and tears everything down. Honours a region crop.
/// </summary>
public static class ScreenshotCapturer
{
    public static ScreenshotImage? Capture(CaptureTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!CaptureCapabilities.IsSupported())
        {
            return null;
        }

        (ID3D11Device device, ID3D11DeviceContext context) = CaptureInterop.CreateDevice();
        using (device)
        using (context)
        {
            IDirect3DDevice winrt = CaptureInterop.CreateWinRtDevice(device);
            GraphicsCaptureItem item = CaptureInterop.CreateItem(target);

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);

            ScreenshotImage? result = null;
            using var got = new ManualResetEventSlim(false);

            framePool.FrameArrived += (pool, _) =>
            {
                using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
                if (frame is null || got.IsSet)
                {
                    return;
                }

                try
                {
                    using ID3D11Texture2D tex = CaptureInterop.GetTexture(frame.Surface);
                    result = Readback(device, context, tex, target.Region);
                }
                finally
                {
                    got.Set();
                }
            };

            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            session.StartCapture();
            got.Wait(2000);
            return result;
        }
    }

    private static unsafe ScreenshotImage Readback(ID3D11Device device, ID3D11DeviceContext context,
        ID3D11Texture2D src, RegionRect? region)
    {
        Texture2DDescription desc = src.Description;
        int srcW = (int)desc.Width, srcH = (int)desc.Height;

        int x = 0, y = 0, w = srcW, h = srcH;
        if (region is { } r)
        {
            x = Math.Clamp(r.X, 0, srcW - 1);
            y = Math.Clamp(r.Y, 0, srcH - 1);
            w = Math.Clamp(r.Width, 1, srcW - x);
            h = Math.Clamp(r.Height, 1, srcH - y);
        }

        using ID3D11Texture2D staging = device.CreateTexture2D(desc with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
        context.CopyResource(staging, src);

        int stride = w * 4;
        byte[] bgra = new byte[stride * h];
        MappedSubresource map = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            byte* basePtr = (byte*)map.DataPointer;
            int rowPitch = (int)map.RowPitch;
            fixed (byte* dst = bgra)
            {
                for (int row = 0; row < h; row++)
                {
                    byte* srcRow = basePtr + (long)(y + row) * rowPitch + (long)x * 4;
                    Buffer.MemoryCopy(srcRow, dst + (long)row * stride, stride, stride);
                }
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        return new ScreenshotImage(w, h, stride, bgra);
    }
}
