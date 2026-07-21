namespace RecMode.Capture;

/// <summary>A sub-rectangle of a monitor, in that monitor's local pixel coordinates (0,0 = top-left).</summary>
public readonly record struct RegionRect(int X, int Y, int Width, int Height);

/// <summary>What a capture session is pointed at — a whole monitor, a single window, or a monitor region.</summary>
public sealed record CaptureTarget
{
    public required CaptureKind Kind { get; init; }

    /// <summary>HMONITOR for monitor/region, HWND for window.</summary>
    public required nint Handle { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Set for <see cref="CaptureKind.Region"/>: the crop rect within the monitor (GPU-cropped in the NV12 pass).</summary>
    public RegionRect? Region { get; init; }

    /// <summary>Set for <see cref="CaptureKind.AllDisplays"/>: the virtual-desktop bounding box (union of every
    /// monitor's rect) — there's no single HMONITOR/GraphicsCaptureItem for "all displays", so callers that would
    /// otherwise ask WGC for the source size (<see cref="CaptureCapabilities.TryGetSourceSize"/>) use this instead.</summary>
    public RegionRect? VirtualDesktopBounds { get; init; }

    public static CaptureTarget FromMonitor(MonitorInfo m) =>
        new() { Kind = CaptureKind.Monitor, Handle = m.Handle, DisplayName = m.DisplayName };

    public static CaptureTarget FromWindow(WindowInfo w) =>
        new() { Kind = CaptureKind.Window, Handle = w.Handle, DisplayName = w.Title };

    public static CaptureTarget FromRegion(MonitorInfo m, RegionRect region) =>
        new() { Kind = CaptureKind.Region, Handle = m.Handle, DisplayName = $"Region {region.Width}×{region.Height}", Region = region };

    public static CaptureTarget FromAllDisplays(IReadOnlyList<MonitorInfo> monitors)
    {
        int minX = monitors.Min(m => m.X);
        int minY = monitors.Min(m => m.Y);
        int width = monitors.Max(m => m.X + m.Width) - minX;
        int height = monitors.Max(m => m.Y + m.Height) - minY;
        return new CaptureTarget
        {
            Kind = CaptureKind.AllDisplays,
            Handle = nint.Zero,
            DisplayName = "All Displays",
            VirtualDesktopBounds = new RegionRect(minX, minY, width, height),
        };
    }
}

public enum CaptureKind
{
    Monitor,
    Window,
    Region,
    AllDisplays,
}

/// <summary>A capturable top-level window (Record screen's window picker).</summary>
public sealed record WindowInfo
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public int ProcessId { get; init; }
    public override string ToString() => Title;
}

/// <summary>Pure geometry for turning a Window capture's on-screen rect into an equivalent Region capture
/// target (a monitor + a local crop rect). WGC's per-window capture (<see cref="CaptureKind.Window"/>) only
/// ever sees that one window's own rendered content — a separate overlay window drawn on top of it (like
/// draw-on-screen annotation) is invisible to it, unlike Monitor/Region/AllDisplays sources where WGC/DDA
/// see the whole screen including overlays. So while annotating a Window source, capture temporarily
/// substitutes the monitor showing the most of that window, cropped to the window's rect, using the exact
/// same GPU-crop mechanism Region sources already use — see <see cref="RecMode.App.Services.RecordingCoordinator.SetAnnotating"/>.</summary>
public static class WindowRegionProxy
{
    public static CaptureTarget? Resolve(RegionRect windowRect, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        MonitorInfo? best = null;
        long bestArea = 0;
        foreach (MonitorInfo mon in monitors)
        {
            long area = OverlapArea(mon, windowRect);
            if (area > bestArea)
            {
                bestArea = area;
                best = mon;
            }
        }

        if (best is not { } m)
        {
            return null;
        }

        // Intersect the window's monitor-local rect with the monitor bounds — clamping the origin alone
        // isn't enough (a window straddling the monitor edge would then keep its full original width/height,
        // cropping past its own true edge into unrelated monitor content beyond it).
        int rawX = windowRect.X - m.X;
        int rawY = windowRect.Y - m.Y;
        int x = Math.Clamp(rawX, 0, m.Width - 1);
        int y = Math.Clamp(rawY, 0, m.Height - 1);
        int right = Math.Clamp(rawX + windowRect.Width, x + 1, m.Width);
        int bottom = Math.Clamp(rawY + windowRect.Height, y + 1, m.Height);
        return CaptureTarget.FromRegion(m, new RegionRect(x, y, right - x, bottom - y));
    }

    private static long OverlapArea(MonitorInfo mon, RegionRect r)
    {
        int x1 = Math.Max(mon.X, r.X);
        int y1 = Math.Max(mon.Y, r.Y);
        int x2 = Math.Min(mon.X + mon.Width, r.X + r.Width);
        int y2 = Math.Min(mon.Y + mon.Height, r.Y + r.Height);
        return x2 > x1 && y2 > y1 ? (long)(x2 - x1) * (y2 - y1) : 0;
    }
}

/// <summary>Pure matching rules for "follow selected window" when an app recreates its top-level HWND.</summary>
public static class WindowFollowResolver
{
    public static WindowInfo? Resolve(WindowInfo current, IReadOnlyList<WindowInfo> candidates)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);

        WindowInfo? sameHandle = candidates.FirstOrDefault(w => w.Handle == current.Handle);
        if (sameHandle is not null)
        {
            return sameHandle;
        }

        if (current.ProcessId > 0)
        {
            WindowInfo? sameProcessAndTitle = candidates.FirstOrDefault(w =>
                w.ProcessId == current.ProcessId &&
                string.Equals(w.Title, current.Title, StringComparison.OrdinalIgnoreCase));
            if (sameProcessAndTitle is not null)
            {
                return sameProcessAndTitle;
            }

            WindowInfo? sameProcess = candidates.FirstOrDefault(w => w.ProcessId == current.ProcessId);
            if (sameProcess is not null)
            {
                return sameProcess;
            }
        }

        return candidates.FirstOrDefault(w =>
            string.Equals(w.Title, current.Title, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A running app with a visible window, usable as a per-app audio capture target (plan §7).</summary>
public sealed record AudioProcessTarget
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string WindowTitle { get; init; }
    public override string ToString() => $"{WindowTitle} ({ProcessName})";
}
