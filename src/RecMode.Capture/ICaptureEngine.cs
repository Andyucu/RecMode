using RecMode.Capture.Webcam;

namespace RecMode.Capture;

/// <summary>
/// Screen capture producing tightly-packed NV12 frames for the encoding pipeline. Lives only while the
/// Record screen is visible or a recording is active (plan §3.9 lifecycle); <see cref="Start"/> spins up
/// WGC + the GPU NV12 converter, <see cref="Stop"/>/<see cref="IDisposable.Dispose"/> tears it all down.
/// </summary>
public interface ICaptureEngine : IDisposable
{
    bool IsRunning { get; }
    int OutputWidth { get; }
    int OutputHeight { get; }

    /// <summary>Tightly-packed NV12 frame size in bytes; the buffer passed to <see cref="TryGetLatestFrame"/> must be this big.</summary>
    int Nv12ByteSize { get; }

    /// <summary>Unique frames delivered by WGC so far (on-change; the pacer duplicates to CFR).</summary>
    long CapturedFrameCount { get; }

    /// <summary>
    /// Raised (never on the calling thread) if a background capture thread (e.g. Desktop Duplication's pump)
    /// fails unexpectedly and had to stop itself. Capture keeps running in a degraded state (frames simply
    /// stop updating) rather than the process crashing; subscribers should surface this to the user.
    /// </summary>
    event EventHandler<Exception>? Faulted;

    /// <summary>Starts capturing <paramref name="target"/>, converting to NV12 scaled to <paramref name="dstW"/>×<paramref name="dstH"/>.</summary>
    void Start(CaptureTarget target, int dstW, int dstH, bool captureCursor);

    /// <summary>Copies the most recent NV12 frame into <paramref name="dest"/>. False until the first frame arrives.</summary>
    bool TryGetLatestFrame(byte[] dest);

    /// <summary>Enables/disables the webcam picture-in-picture overlay on this capture's output; call after <see cref="Start"/>. Null source disables it.</summary>
    void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect);

    /// <summary>Sets the captured-video brightness adjustment, -100..100, 0 = unchanged; call before or after <see cref="Start"/>.</summary>
    void SetBrightness(double value);

    void Stop();
}

/// <summary>Static discovery that doesn't require an engine instance.</summary>
public static class CaptureCapabilities
{
    public static IReadOnlyList<MonitorInfo> EnumerateMonitors() => CaptureInterop.EnumerateMonitors();

    public static IReadOnlyList<WindowInfo> EnumerateWindows() => CaptureInterop.EnumerateWindows();

    public static IReadOnlyList<AudioProcessTarget> EnumerateAudioProcesses() => CaptureInterop.EnumerateAudioProcesses();

    public static bool IsSupported() => Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();

    /// <summary>Current on-screen bounds of any top-level window, in absolute virtual-desktop physical pixels
    /// — used to keep overlay windows (e.g. draw-on-screen annotation) from covering other floating chrome.</summary>
    public static bool TryGetWindowScreenRect(nint hwnd, out RegionRect rect) => CaptureInterop.TryGetWindowRect(hwnd, out rect);

    /// <summary>The topmost capturable window whose bounds contain the given absolute virtual-desktop physical
    /// pixel point, or null. Used by the "pick a window with the mouse" picker: <see cref="EnumerateWindows"/>
    /// already returns windows in top-to-bottom Z-order and already excludes title-less windows (which is how
    /// RecMode's own transient overlays — including the picker itself — stay out of the way), so the first
    /// bounds match is the topmost real window under the cursor.</summary>
    public static WindowInfo? FindWindowAtPoint(int x, int y)
    {
        foreach (WindowInfo w in EnumerateWindows())
        {
            if (CaptureInterop.TryGetWindowRect(w.Handle, out RegionRect r) &&
                x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height)
            {
                return w;
            }
        }

        return null;
    }

    /// <summary>Absolute virtual-desktop physical-pixel bounds of a capture target, when resolvable — used to
    /// size overlay windows (draw-on-screen annotation) so they only cover what's actually being recorded
    /// rather than always the primary monitor.</summary>
    public static bool TryGetScreenBounds(CaptureTarget target, out RegionRect bounds)
    {
        if (target.Kind == CaptureKind.AllDisplays)
        {
            if (target.VirtualDesktopBounds is { } vdb)
            {
                bounds = vdb;
                return true;
            }
        }
        else if (target.Kind == CaptureKind.Window)
        {
            if (CaptureInterop.TryGetWindowRect(target.Handle, out bounds))
            {
                return true;
            }
        }
        else
        {
            MonitorInfo? mon = EnumerateMonitors().FirstOrDefault(m => m.Handle == target.Handle);
            if (mon is not null)
            {
                bounds = target.Region is { } region
                    ? new RegionRect(mon.X + region.X, mon.Y + region.Y, region.Width, region.Height)
                    : new RegionRect(mon.X, mon.Y, mon.Width, mon.Height);
                return true;
            }
        }

        bounds = default;
        return false;
    }

    /// <summary>Resolves the current pixel size of a capture target (used to compute the encoded output size).</summary>
    public static bool TryGetSourceSize(CaptureTarget target, out int width, out int height)
    {
        if (target.Kind == CaptureKind.AllDisplays)
        {
            width = target.VirtualDesktopBounds?.Width ?? 0;
            height = target.VirtualDesktopBounds?.Height ?? 0;
            return width > 0 && height > 0;
        }

        if (target.Region is { } region)
        {
            width = region.Width;
            height = region.Height;
            return width > 0 && height > 0;
        }

        try
        {
            Windows.Graphics.Capture.GraphicsCaptureItem item = CaptureInterop.CreateItem(target);
            width = item.Size.Width;
            height = item.Size.Height;
            return width > 0 && height > 0;
        }
        catch (Exception)
        {
            width = 0;
            height = 0;
            return false;
        }
    }
}
