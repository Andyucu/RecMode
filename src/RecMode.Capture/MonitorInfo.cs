namespace RecMode.Capture;

/// <summary>A capturable display, as surfaced in the Record screen's display picker.</summary>
public sealed record MonitorInfo
{
    /// <summary>Native monitor handle (HMONITOR) — used to create the WGC capture item.</summary>
    public required nint Handle { get; init; }

    /// <summary>Human label, e.g. "Display 1 (5120 × 1440)".</summary>
    public required string DisplayName { get; init; }

    /// <summary>Device name, e.g. <c>\\.\DISPLAY1</c>.</summary>
    public required string DeviceName { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }

    public override string ToString() => DisplayName;
}
