using System.Runtime.InteropServices;
using RecMode.Capture.Webcam;

namespace RecMode.Capture;

/// <summary>
/// Compatibility capture path for VMs, RDP sessions, and machines without a usable WGC/D3D11 device.
/// It uses the Windows GDI desktop surface and converts BGRA to the same tightly-packed NV12 contract as
/// the GPU engine. It is intentionally a fallback: CPU capture is slower and does not support webcam
/// compositing, but it keeps basic recording functional on virtual displays.
/// </summary>
internal sealed class GdiCaptureEngine : ICaptureEngine
{
    private const int FallbackFramesPerSecond = 30;
    private readonly object _sync = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private byte[] _latest = [];
    private RegionRect _bounds;
    private int _dstW, _dstH;
    private nint _windowHandle;
    private bool _captureWindow;
    private bool _captureCursor;
    private bool _hasLatest;

    public bool IsRunning { get; private set; }
    public int OutputWidth => _dstW;
    public int OutputHeight => _dstH;
    public int Nv12ByteSize => _dstW * _dstH * 3 / 2;
    public long CapturedFrameCount { get; private set; }
    public bool SupportsZoom => false;
    public bool HdrToneMapActive => false;
    public event EventHandler<Exception>? Faulted;

    public void Start(CaptureTarget target, int dstW, int dstH, bool captureCursor)
    {
        if (!CaptureInterop.TryGetCaptureBounds(target, out _bounds))
            throw new InvalidOperationException("The selected source has no screen bounds.");

        _dstW = dstW;
        _dstH = dstH;
        _latest = new byte[Nv12ByteSize];
        _windowHandle = target.Handle;
        _captureWindow = target.Kind == CaptureKind.Window;
        _captureCursor = captureCursor;
        _hasLatest = false;
        CapturedFrameCount = 0;
        _stopping = false;
        IsRunning = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "recmode-gdi" };
        _thread.Start();
    }

    private void CaptureLoop()
    {
        nint screen = IntPtr.Zero, dc = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            int srcStride = _bounds.Width * 4;
            byte[] bgra = new byte[srcStride * _bounds.Height];
            byte[] nv12 = new byte[Nv12ByteSize];
            screen = GetDC(IntPtr.Zero);
            dc = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFO { Header = new BITMAPINFOHEADER { Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = _bounds.Width, Height = -_bounds.Height, Planes = 1, BitCount = 32, Compression = 0 } };
            bitmap = CreateDIBSection(dc, ref bmi, 0, out nint bits, IntPtr.Zero, 0);
            if (screen == IntPtr.Zero || dc == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("GDI could not allocate the desktop capture surface.");
            old = SelectObject(dc, bitmap);
            long intervalTicks = System.Diagnostics.Stopwatch.Frequency / FallbackFramesPerSecond;
            long nextFrame = System.Diagnostics.Stopwatch.GetTimestamp();
            while (!_stopping)
            {
                CaptureBgra(dc, bits, bgra, srcStride);
                ConvertToNv12(bgra, _bounds.Width, _bounds.Height, nv12);
                lock (_sync)
                {
                    Buffer.BlockCopy(nv12, 0, _latest, 0, nv12.Length);
                    _hasLatest = true;
                }
                CapturedFrameCount++;
                nextFrame += intervalTicks;
                long remaining = nextFrame - System.Diagnostics.Stopwatch.GetTimestamp();
                if (remaining > 0) Thread.Sleep(Math.Max(1, (int)(remaining * 1000 / System.Diagnostics.Stopwatch.Frequency)));
                else nextFrame = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, ex);
        }
        finally
        {
            if (old != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (screen != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void CaptureBgra(nint dc, nint bits, byte[] pixels, int stride)
    {
        RegionRect r = _bounds;
        nint screen = GetDC(IntPtr.Zero);
        try
        {
            // BitBlt is correct for desktop sources. CAPTUREBLT includes layered windows such as
            // translucent menus and overlays, which SRCCOPY alone can omit.
            if (_captureWindow)
            {
                // Copy the selected HWND itself instead of its desktop rectangle. This preserves
                // WGC's isolated-window semantics when another application overlaps the window.
                if (!PrintWindow(_windowHandle, dc, PW_RENDERFULLCONTENT))
                    throw new InvalidOperationException("GDI could not capture the selected window.");
            }
            else if (!BitBlt(dc, 0, 0, r.Width, r.Height, screen, r.X, r.Y, SRCCOPY | CAPTUREBLT))
            {
                throw new InvalidOperationException("GDI could not copy the virtual display.");
            }

            if (_captureCursor)
                DrawCursor(dc, r);
            Marshal.Copy(bits, pixels, 0, stride * r.Height);
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static void DrawCursor(nint dc, RegionRect bounds)
    {
        var cursor = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursor) || cursor.flags != CURSOR_SHOWING || cursor.hCursor == IntPtr.Zero)
            return;

        if (!GetIconInfo(cursor.hCursor, out ICONINFO icon))
            return;

        try
        {
            int x = cursor.ptScreenPos.X - (int)icon.xHotspot;
            int y = cursor.ptScreenPos.Y - (int)icon.yHotspot;
            _ = DrawIconEx(dc, x - bounds.X, y - bounds.Y, cursor.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
        }
        finally
        {
            if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
            if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
        }
    }

    private void ConvertToNv12(byte[] bgra, int srcW, int srcH, byte[] output)
    {
        int ySize = _dstW * _dstH;
        for (int y = 0; y < _dstH; y++)
        for (int x = 0; x < _dstW; x++)
        {
            (byte b, byte g, byte r) = Pixel(bgra, srcW, srcH, x, y);
            output[y * _dstW + x] = (byte)Math.Clamp((77 * r + 150 * g + 29 * b + 128) >> 8, 0, 255);
        }
        for (int y = 0; y < _dstH; y += 2)
        for (int x = 0; x < _dstW; x += 2)
        {
            int sumU = 0, sumV = 0;
            for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
            {
                (byte b, byte g, byte r) = Pixel(bgra, srcW, srcH, x + dx, y + dy);
                sumU += ((-43 * r - 85 * g + 128 * b + 32768) >> 8);
                sumV += ((128 * r - 107 * g - 21 * b + 32768) >> 8);
            }
            int at = ySize + (y / 2) * _dstW + x;
            output[at] = (byte)Math.Clamp(sumU / 4, 0, 255);
            output[at + 1] = (byte)Math.Clamp(sumV / 4, 0, 255);
        }
    }

    private (byte B, byte G, byte R) Pixel(byte[] p, int w, int h, int x, int y)
    {
        int sx = Math.Min(w - 1, x * w / _dstW), sy = Math.Min(h - 1, y * h / _dstH);
        int i = (sy * w + sx) * 4;
        return (p[i], p[i + 1], p[i + 2]);
    }

    public bool TryGetLatestFrame(byte[] dest) { lock (_sync) { if (!_hasLatest) return false; Buffer.BlockCopy(_latest, 0, dest, 0, _latest.Length); return true; } }
    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect) { }
    public void SetBrightness(double value) { }
    public void SetZoomTarget(RegionRect? rect) { } // no VideoProcessor pass to crop in the GDI fallback path
    public void SetBaseRect(RegionRect rect) { }
    public void Stop() { IsRunning = false; _stopping = true; _thread?.Join(2000); _thread = null; }
    public void Dispose() => Stop();

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const int CURSOR_SHOWING = 0x00000001;
    private const int DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER Header; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize, flags; public nint hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public bool fIcon; public uint xHotspot, yHotspot; public nint hbmMask, hbmColor; }
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO cursorInfo);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int xLeft, int yTop, nint icon, int cxWidth, int cyWidth, uint istepIfAniCur, nint hbrFlickerFreeDraw, int diFlags);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out ICONINFO iconInfo);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BITMAPINFO bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
}
