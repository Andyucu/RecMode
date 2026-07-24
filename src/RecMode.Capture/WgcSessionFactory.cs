using Vortice.Direct3D11;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace RecMode.Capture;

/// <summary>
/// Shared WGC session bring-up behind <see cref="WgcCaptureEngine"/> (recording, NV12) and
/// <see cref="WgcPreviewEngine"/> (preview, BGRA scaled) — D3D11 device creation, the WinRT device wrapper,
/// the capture item, and the frame-pool/session start sequence are identical between the two; only the
/// converter/scaler each wraps around the resulting frames differs.
/// </summary>
internal static class WgcSessionFactory
{
    public readonly record struct Session(
        ID3D11Device Device,
        ID3D11DeviceContext Context,
        GraphicsCaptureItem Item,
        Direct3D11CaptureFramePool FramePool,
        GraphicsCaptureSession CaptureSession);

    public static Session Start(CaptureTarget target, bool captureCursor,
        TypedEventHandler<Direct3D11CaptureFramePool, object?> onFrameArrived, bool preferHdr = false)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;
        try
        {
            (device, context) = CaptureInterop.CreateDevice();
            IDirect3DDevice winrt = CaptureInterop.CreateWinRtDevice(device);
            GraphicsCaptureItem item = CaptureInterop.CreateItem(target);
            // HDR sources: request the frame pool's native FP16 scRGB format instead of 8-bit BGRA, so the
            // captured texture carries the real linear HDR values DWM composited (not an already-clamped 8bpc
            // approximation) — the VideoProcessor pipeline then does the actual tone-map down to SDR
            // (VideoProcessorPipeline's color-space setup). Requesting BGRA8 on an HDR desktop is exactly what
            // produces the washed-out/wrong-color recordings this feature exists to fix.
            DirectXPixelFormat pixelFormat = preferHdr
                ? DirectXPixelFormat.R16G16B16A16Float
                : DirectXPixelFormat.B8G8R8A8UIntNormalized;
            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrt, pixelFormat, 2, item.Size);
            framePool.FrameArrived += onFrameArrived;
            session = framePool.CreateCaptureSession(item);
            CaptureSessionConfig.Apply(session, captureCursor);
            session.StartCapture();
            return new Session(device, context, item, framePool, session);
        }
        catch
        {
            if (framePool is not null) framePool.FrameArrived -= onFrameArrived;
            session?.Dispose();
            framePool?.Dispose();
            context?.Dispose();
            device?.Dispose();
            throw;
        }
    }
}
