namespace RecMode.Capture;

/// <summary>
/// The union bounding box of every monitor's rect (Windows' "virtual desktop" — monitors can sit at negative
/// coordinates relative to the primary). Pure and shared: <see cref="CaptureTarget.FromAllDisplays"/> and
/// <see cref="DesktopDuplicationCaptureSource"/> independently computed the identical min/max math before this
/// was extracted — a real duplication risk since the dev machine's single monitor only ever exercises the
/// trivial <c>minX = minY = 0</c> case, exactly where a sign error in either copy would be invisible.
/// </summary>
public static class VirtualDesktopLayout
{
    public readonly record struct Bounds(int MinX, int MinY, int Width, int Height);

    public static Bounds Compute(IReadOnlyList<MonitorInfo> monitors)
    {
        int minX = monitors.Min(m => m.X);
        int minY = monitors.Min(m => m.Y);
        int width = monitors.Max(m => m.X + m.Width) - minX;
        int height = monitors.Max(m => m.Y + m.Height) - minY;
        return new Bounds(minX, minY, width, height);
    }

    /// <summary>A monitor's placement within the shared canvas <see cref="DesktopDuplicationCaptureSource"/>
    /// composites into — its virtual-desktop origin re-anchored so <paramref name="bounds"/>'s own top-left
    /// lands at (0,0).</summary>
    public static (int OffsetX, int OffsetY) OffsetOf(MonitorInfo monitor, Bounds bounds) =>
        (monitor.X - bounds.MinX, monitor.Y - bounds.MinY);
}
