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
        TypedEventHandler<Direct3D11CaptureFramePool, object?> onFrameArrived)
    {
        (ID3D11Device device, ID3D11DeviceContext context) = CaptureInterop.CreateDevice();
        IDirect3DDevice winrt = CaptureInterop.CreateWinRtDevice(device);
        GraphicsCaptureItem item = CaptureInterop.CreateItem(target);

        Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        framePool.FrameArrived += onFrameArrived;

        GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        CaptureSessionConfig.Apply(session, captureCursor);
        session.StartCapture();

        return new Session(device, context, item, framePool, session);
    }
}
