using RecMode.Capture;

namespace RecMode.App.Services;

/// <summary>Shows the region-select overlay for a monitor and returns the chosen region (monitor-local pixels), or null if cancelled.</summary>
public interface IRegionPicker
{
    /// <param name="excludeFromCapture">True to hide the picker overlay itself from any in-progress recording
    /// (manual zoom, picked while already recording) — false (the default) for the ordinary pre-recording
    /// Region-source picker, which has nothing running yet to worry about capturing into.</param>
    RegionRect? Pick(MonitorInfo monitor, bool excludeFromCapture = false);
}
