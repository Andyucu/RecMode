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

    public static CaptureTarget FromMonitor(MonitorInfo m) =>
        new() { Kind = CaptureKind.Monitor, Handle = m.Handle, DisplayName = m.DisplayName };

    public static CaptureTarget FromWindow(WindowInfo w) =>
        new() { Kind = CaptureKind.Window, Handle = w.Handle, DisplayName = w.Title };

    public static CaptureTarget FromRegion(MonitorInfo m, RegionRect region) =>
        new() { Kind = CaptureKind.Region, Handle = m.Handle, DisplayName = $"Region {region.Width}×{region.Height}", Region = region };
}

public enum CaptureKind
{
    Monitor,
    Window,
    Region,
}

/// <summary>A capturable top-level window (Record screen's window picker).</summary>
public sealed record WindowInfo
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public override string ToString() => Title;
}

/// <summary>A running app with a visible window, usable as a per-app audio capture target (plan §7).</summary>
public sealed record AudioProcessTarget
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string WindowTitle { get; init; }
    public override string ToString() => $"{WindowTitle} ({ProcessName})";
}
