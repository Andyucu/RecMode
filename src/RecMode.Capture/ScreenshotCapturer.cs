using Serilog;
using System.Runtime.InteropServices;
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

        if (target.Kind == CaptureKind.Webcam)
        {
            return CaptureWebcam(target);
        }

        // IsSupported() only answers "does this Windows build expose the WGC API" — it is NOT a promise that
        // a D3D11 device can actually be created. The dominant real-world failure (RDP sessions, VMs, driver
        // feature-level limits) passes this check and then throws DXGI_ERROR_UNSUPPORTED out of
        // CreateDevice(), which is exactly the population the GDI fallback exists for. So the fallback has to
        // be reachable from a thrown exception too, not just from the pre-check — mirroring
        // WgcCaptureEngine.Start, which has always caught around WgcSessionFactory.Start for this same
        // reason. A null return (the 2s frame timeout) falls back as well: a blank screenshot and no
        // screenshot are equally useless to the user.
        if (!CaptureCapabilities.IsSupported())
        {
            return CaptureGdi(target);
        }

        try
        {
            return (target.Kind == CaptureKind.AllDisplays ? CaptureAllDisplays() : CaptureWgc(target))
                ?? CaptureGdi(target);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WGC screenshot failed; falling back to GDI");
            return CaptureGdi(target);
        }
    }

    private static ScreenshotImage? CaptureWgc(CaptureTarget target)
    {
        (ID3D11Device device, ID3D11DeviceContext context) = CaptureInterop.CreateDevice();
        using (device)
        using (context)
        {
            // Only needed transiently to hand off to CreateFreeThreaded below; never disposed before, so
            // every screenshot leaked one wrapper holding its own native reference to `device` until GC
            // finalized it — see WgcSessionFactory.Start's identical fix for the same pattern.
            using IDirect3DDevice winrt = CaptureInterop.CreateWinRtDevice(device);
            GraphicsCaptureItem item = CaptureInterop.CreateItem(target);

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);

            ScreenshotImage? result = null;
            using var got = new ManualResetEventSlim(false);
            // Barrier against a FrameArrived callback still in flight when got.Wait(2000) below times out
            // rather than being signaled: WGC can deliver a frame right at the 2s mark, and without this the
            // device/context/framePool/session get disposed by the using blocks above while the callback is
            // still inside Readback() using them — a use-after-dispose that isn't even catchable (it's a COM
            // access violation on a threadpool thread, i.e. a process crash), not merely a bad result. Taking
            // this lock after the wait blocks until any in-flight callback has actually finished, exactly the
            // same barrier WgcCaptureEngine/WgcPreviewEngine's Stop() already use against their own
            // FrameArrived callbacks.
            var disposeGuard = new Lock();

            framePool.FrameArrived += (pool, _) =>
            {
                lock (disposeGuard)
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
                    catch (Exception ex)
                    {
                        // Never let an exception escape a WinRT event callback running on a threadpool
                        // thread — unlike a Task, that's a genuinely unhandled exception that terminates the
                        // process immediately, for what should just be a failed screenshot.
                        Log.Warning(ex, "Screenshot readback failed");
                    }
                    finally
                    {
                        got.Set();
                    }
                }
            };

            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            session.StartCapture();
            got.Wait(2000);

            // Stop delivery BEFORE the barrier below, not just wait-then-barrier: got.Wait(2000) can time out
            // (return false) with no callback ever having been dispatched, and the FrameArrived subscription
            // is still live at that point — WGC can still deliver a frame right at/after the 2s mark. Without
            // disposing the session/pool here first, that late callback would enter the disposeGuard lock
            // completely uncontested and reach Readback() using device/context objects the enclosing `using`
            // blocks are about to release the moment this method returns — a use-after-dispose that's a COM
            // access violation on a threadpool thread (an uncatchable process crash), not just a bad result.
            // WinRT IClosable types tolerate a repeat Dispose() from the `using` blocks above as a no-op.
            session.Dispose();
            framePool.Dispose();

            // Drains any callback that had already started (and taken the lock) before the disposal above
            // could take effect.
            lock (disposeGuard) { }
            return result;
        }
    }

    /// <summary>Webcam-as-source: no WGC item exists for a camera either, so grab one BGRA frame directly
    /// from <see cref="Webcam.WebcamCaptureSource"/> (same primitive <see cref="Webcam.WebcamCaptureEngine"/>
    /// uses for recording) instead of routing through D3D11 at all.</summary>
    private static ScreenshotImage? CaptureWebcam(CaptureTarget target)
    {
        if (target.WebcamDeviceId is not { } deviceId)
        {
            return null;
        }

        var source = new Webcam.WebcamCaptureSource();
        try
        {
            source.StartAsync(deviceId).GetAwaiter().GetResult();
            byte[] bgra = [];
            for (int i = 0; i < 50; i++) // up to ~1s for the first real frame to arrive
            {
                // TryGetLatestFrame copies into (and sizes) bgra under the source's own lock, so this is
                // already a private, tear-free snapshot — no second copy needed.
                if (source.TryGetLatestFrame(ref bgra, out int w, out int h, out int stride) && w > 0 && h > 0)
                {
                    return new ScreenshotImage(w, h, stride, bgra);
                }
                Thread.Sleep(20);
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            source.Stop();
        }
    }

    /// <summary>"All Displays": no WGC item exists for this, so grab one composited frame via DXGI Desktop
    /// Duplication instead (same primitive the recording/preview engines use).</summary>
    private static ScreenshotImage? CaptureAllDisplays()
    {
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        using var dda = new DesktopDuplicationCaptureSource(monitors);
        using (dda.Device)
        using (dda.Context)
        {
            // The very first AcquireNextFrame per output can legitimately time out rather than deliver
            // current content immediately (DXGI only signals on an actual desktop change) — a few extra
            // pulls make sure every output has produced at least one real frame before the readback below.
            ID3D11Texture2D canvas = dda.AcquireNextFrame(timeoutMs: 500, out _);
            for (int i = 0; i < 4; i++)
            {
                canvas = dda.AcquireNextFrame(timeoutMs: 200, out _);
            }
            return Readback(dda.Device, dda.Context, canvas, region: null);
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

    private static ScreenshotImage? CaptureGdi(CaptureTarget target)
    {
        if (!CaptureInterop.TryGetCaptureBounds(target, out var bounds))
        {
            return null;
        }

        nint screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;

        nint dc = CreateCompatibleDC(screen);
        if (dc == IntPtr.Zero)
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
            return null;
        }

        var bmi = new BITMAPINFO { Header = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = bounds.Width, Height = -bounds.Height, Planes = 1, BitCount = 32, Compression = 0 } };
        nint bitmap = CreateDIBSection(dc, ref bmi, 0, out nint bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            DeleteDC(dc);
            _ = ReleaseDC(IntPtr.Zero, screen);
            return null;
        }

        nint old = SelectObject(dc, bitmap);
        try
        {
            bool captured;
            if (target.Kind == CaptureKind.Window)
            {
                captured = PrintWindow(target.Handle, dc, PW_RENDERFULLCONTENT);
            }
            else
            {
                captured = BitBlt(dc, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, SRCCOPY | CAPTUREBLT);
            }

            if (!captured)
            {
                return null;
            }

            GdiFlush();

            int stride = bounds.Width * 4;
            byte[] bgra = new byte[stride * bounds.Height];
            unsafe
            {
                fixed (byte* dst = bgra)
                {
                    Buffer.MemoryCopy((void*)bits, dst, bgra.Length, bgra.Length);
                }
            }

            return new ScreenshotImage(bounds.Width, bounds.Height, stride, bgra);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER Header; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant; }
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BITMAPINFO bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
}
