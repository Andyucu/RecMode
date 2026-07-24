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

    /// <summary>Monitor origin in virtual-desktop pixels (top-left).</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }

    /// <summary>True if Windows currently reports Advanced Color (HDR10) active on this monitor — drives
    /// whether the capture pipeline applies HDR-to-SDR tone mapping (§3.6).</summary>
    public bool IsHdr { get; init; }

    /// <summary>True for the synthetic "All Displays" entry appended to the Record screen's display picker
    /// when more than one real monitor is present (plan §1: "Full screen (per display + all displays)").
    /// <see cref="Handle"/> is unused for this entry; X/Y/Width/Height carry the virtual-desktop bounding box.</summary>
    public bool IsAllDisplays { get; init; }

    public override string ToString() => DisplayName;
}
