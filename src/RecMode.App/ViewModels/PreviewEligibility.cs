namespace RecMode.App.ViewModels;

/// <summary>
/// The §3.9 "can preview/meter run right now" predicate — pure and centralized after being found
/// independently hand-copied in four places (<see cref="RecordViewModel"/>'s Preview/Audio partials, the main
/// file's <c>TryResumeAfterVisibilityChange</c>, and <c>SourceContourService</c>), with zero tests, despite
/// this exact combination having already regressed twice (preview/metering running from a <c>--tray</c>
/// launch or a hidden window). Extracted so every call site shares one tested implementation instead of
/// four copies that can silently drift apart.
/// </summary>
internal static class PreviewEligibility
{
    /// <summary>True only when a window that actually displays preview/meter surfaces is shown, active, and
    /// not minimized — see <see cref="RecordViewModel.SetWindowVisible"/> for why all four flags exist.</summary>
    public static bool CanRun(bool isActivePage, bool isWindowMinimized, bool isWindowVisible, bool hostsPreviewSurfaces) =>
        isActivePage && !isWindowMinimized && isWindowVisible && hostsPreviewSurfaces;
}
