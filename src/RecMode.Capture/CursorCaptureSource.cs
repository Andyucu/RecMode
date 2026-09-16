using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace RecMode.Capture;

/// <summary>
/// Samples the real Windows cursor and publishes it as a BGRA frame plus a smoothed source-space position,
/// for the GPU compositor to draw instead of the OS cursor (plan §7 "smooth cursor"). The point is that the
/// raw pointer position arrives as sparse, stepped samples; compositing our own lets the motion be eased and
/// the cursor scaled.
/// <para>
/// Only ever started on a capture path that can actually composite it (see <c>ICaptureEngine</c>'s cursor
/// members): the OS cursor is suppressed while this runs, so on a path that cannot composite, the pointer
/// would disappear from the recording entirely.
/// </para>
/// </summary>
public sealed class CursorCaptureSource : ICursorFrameSource, IDisposable
{
    private const int PollIntervalMs = 6; // ~165 Hz: comfortably finer than any frame rate we render at

    private readonly RegionRect _bounds;
    private readonly double _halfLifeSeconds;
    private readonly object _sync = new();
    private readonly Thread _thread;
    private volatile bool _stopping;
    private readonly ManualResetEventSlim _stopped = new(false);

    private byte[] _frame = [];
    private int _width, _height, _stride;
    private long _sequence;
    private bool _hasPosition;
    private double _smoothX, _smoothY;
    private long _lastTimestamp;
    private nint _lastShape;
    private int _lastShapeWidth, _lastShapeHeight;

    public CursorCaptureSource(RegionRect textureBounds, double halfLifeSeconds = CursorSmoothing.DefaultHalfLifeSeconds)
    {
        _bounds = textureBounds;
        _halfLifeSeconds = halfLifeSeconds;
        _thread = new Thread(Loop) { IsBackground = true, Name = "recmode-cursor" };
    }

    public void Start() => _thread.Start();

    public bool TryGetLatestFrame(ref byte[] destination, out int width, out int height, out int stride)
    {
        lock (_sync)
        {
            width = _width;
            height = _height;
            stride = _stride;
            if (_sequence == 0 || width <= 0 || height <= 0 || _frame.Length < stride * height)
            {
                return false;
            }

            int needed = stride * height;
            if (destination.Length < needed)
            {
                destination = new byte[needed];
            }

            Buffer.BlockCopy(_frame, 0, destination, 0, needed);
            return true;
        }
    }

    public long FrameSequence => Interlocked.Read(ref _sequence);

    public bool TryGetPosition(out int x, out int y)
    {
        lock (_sync)
        {
            if (!_hasPosition)
            {
                x = y = 0;
                return false;
            }

            x = (int)Math.Round(_smoothX);
            y = (int)Math.Round(_smoothY);
            return true;
        }
    }

    /// <summary>Stops publishing a position while the OS cursor is hidden (typing, full-screen video), so the
    /// recording doesn't keep drawing a stale pointer.</summary>
    private void ClearPosition()
    {
        lock (_sync)
        {
            _hasPosition = false;
        }
    }

    private void Loop()
    {
        nint screen = IntPtr.Zero, dc = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero, bits = IntPtr.Zero;
        int dibWidth = 0, dibHeight = 0;
        var cursorInfo = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        _lastTimestamp = Stopwatch.GetTimestamp();

        try
        {
            screen = GetDC(IntPtr.Zero);
            dc = CreateCompatibleDC(screen);

            while (!_stopping)
            {
                if (!GetCursorInfo(ref cursorInfo) ||
                    cursorInfo.flags != CURSOR_SHOWING ||
                    cursorInfo.hCursor == IntPtr.Zero ||
                    !GetIconInfo(cursorInfo.hCursor, out ICONINFO icon))
                {
                    ClearPosition();
                    Thread.Sleep(PollIntervalMs);
                    continue;
                }

                try
                {
                    if (!TryGetIconSize(icon, out int iconWidth, out int iconHeight) || iconWidth <= 0 || iconHeight <= 0)
                    {
                        ClearPosition();
                        // Sleep before retrying, don't just `continue`: the loop's own Thread.Sleep sits after
                        // the try/finally, so a persistent failure here (a cursor whose size can't be read,
                        // GDI handle exhaustion) span this thread at 100% of a core with nothing to show for
                        // it. §3.9 — and the same CPU-spin shape this project has already been bitten by once.
                        Thread.Sleep(PollIntervalMs);
                        continue;
                    }

                    // (Re)create the DIB only when the cursor image's size changes — the overwhelmingly common
                    // case is one shape for a whole recording.
                    if (bitmap == IntPtr.Zero || dibWidth != iconWidth || dibHeight != iconHeight)
                    {
                        if (old != IntPtr.Zero && dc != IntPtr.Zero)
                        {
                            SelectObject(dc, old);
                            old = IntPtr.Zero;
                        }

                        if (bitmap != IntPtr.Zero)
                        {
                            DeleteObject(bitmap);
                        }

                        var bmi = new BITMAPINFO
                        {
                            Header = new BITMAPINFOHEADER
                            {
                                Size = Marshal.SizeOf<BITMAPINFOHEADER>(),
                                Width = iconWidth,
                                Height = -iconHeight, // top-down, matching the frame layout
                                Planes = 1,
                                BitCount = 32,
                                Compression = 0,
                            },
                        };
                        bitmap = CreateDIBSection(dc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                        {
                            ClearPosition();
                            Thread.Sleep(PollIntervalMs); // see the sleep note above — never spin on failure
                            continue;
                        }

                        old = SelectObject(dc, bitmap);
                        dibWidth = iconWidth;
                        dibHeight = iconHeight;
                    }

                    int stride = iconWidth * 4;
                    if (cursorInfo.hCursor != _lastShape || iconWidth != _lastShapeWidth || iconHeight != _lastShapeHeight)
                    {
                        // Shape changed: clear, redraw, and republish the pixels. DrawIconEx composites the
                        // cursor's mask and colour planes for us, including the alpha channel for 32-bit
                        // cursors, which is why this isn't hand-rolled from hbmMask/hbmColor.
                        unsafe
                        {
                            new Span<byte>((void*)bits, stride * iconHeight).Clear();
                        }

                        DrawIconEx(dc, 0, 0, cursorInfo.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);

                        var frame = new byte[stride * iconHeight];
                        Marshal.Copy(bits, frame, 0, frame.Length);
                        lock (_sync)
                        {
                            _frame = frame;
                            _width = iconWidth;
                            _height = iconHeight;
                            _stride = stride;
                            Interlocked.Increment(ref _sequence);
                        }

                        _lastShape = cursorInfo.hCursor;
                        _lastShapeWidth = iconWidth;
                        _lastShapeHeight = iconHeight;
                    }

                    // Position, hotspot-adjusted (the image's top-left, not the click point) and converted from
                    // screen space into the capture texture's own space.
                    double targetX = cursorInfo.ptScreenPos.X - icon.xHotspot - _bounds.X;
                    double targetY = cursorInfo.ptScreenPos.Y - icon.yHotspot - _bounds.Y;

                    long now = Stopwatch.GetTimestamp();
                    double deltaSeconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
                    _lastTimestamp = now;

                    lock (_sync)
                    {
                        if (!_hasPosition)
                        {
                            // First sample after being hidden: start AT the target, so the pointer doesn't
                            // visibly fly in from wherever it was last drawn.
                            _smoothX = targetX;
                            _smoothY = targetY;
                            _hasPosition = true;
                        }
                        else
                        {
                            _smoothX = CursorSmoothing.Advance(_smoothX, targetX, deltaSeconds, _halfLifeSeconds);
                            _smoothY = CursorSmoothing.Advance(_smoothY, targetY, deltaSeconds, _halfLifeSeconds);
                        }
                    }
                }
                finally
                {
                    if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
                    if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
                }

                Thread.Sleep(PollIntervalMs);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cursor sampling ended");
        }
        finally
        {
            if (old != IntPtr.Zero && dc != IntPtr.Zero) SelectObject(dc, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (screen != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, screen);
            _stopped.Set();
        }
    }

    /// <summary>Colour cursors report their size through <c>hbmColor</c>; monochrome ones store the colour and
    /// mask planes stacked in <c>hbmMask</c> (hence the halved height).</summary>
    private static bool TryGetIconSize(ICONINFO icon, out int width, out int height)
    {
        width = height = 0;
        nint reference = icon.hbmColor != IntPtr.Zero ? icon.hbmColor : icon.hbmMask;
        if (reference == IntPtr.Zero)
        {
            return false;
        }

        if (GetObject(reference, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap) == 0)
        {
            return false;
        }

        width = bitmap.bmWidth;
        height = icon.hbmColor != IntPtr.Zero ? bitmap.bmHeight : bitmap.bmHeight / 2;
        return true;
    }

    public void Stop()
    {
        _stopping = true;
        if (_thread.IsAlive && !_stopped.Wait(500))
        {
            Log.Debug("Cursor sampling thread didn't stop within 500 ms");
        }
    }

    public void Dispose() => Stop();

    private const uint CURSOR_SHOWING = 0x00000001;
    private const uint DI_NORMAL = 0x0003;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize, flags; public nint hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool fIcon; public uint xHotspot, yHotspot; public nint hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public nint bmBits; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER Header; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant; }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO cursorInfo);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out ICONINFO iconInfo);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int cx, int cy, uint step, nint brush, uint flags);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BITMAPINFO bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int GetObject(nint obj, int size, out BITMAP bitmap);
}
