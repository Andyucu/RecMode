using RecMode.Capture;

namespace RecMode.App.Services;

/// <summary>Shows the region-select overlay for a monitor and returns the chosen region (monitor-local pixels), or null if cancelled.</summary>
public interface IRegionPicker
{
    RegionRect? Pick(MonitorInfo monitor);
}
