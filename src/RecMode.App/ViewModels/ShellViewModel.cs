using CommunityToolkit.Mvvm.ComponentModel;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.App.ViewModels;

/// <summary>
/// The shell's view model. In Phase 0 it surfaces a foundation health summary (mode, OS build, ffmpeg
/// status, crash recovery) so the scaffolding is demoable and every Core service is exercised end-to-end.
/// Phase 1 replaces the body with real navigation. Properties are set once at construction, so plain
/// auto-properties suffice here; observable state arrives with the interactive views.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    public ShellViewModel(
        IAppPaths paths,
        IOsCapabilities os,
        ISettingsService settings,
        IFfmpegLocator ffmpeg,
        ICrashReporter crash)
    {
        Title = "RecMode";
        ModeLine = paths.IsPortable ? "Portable mode" : "Installed mode";
        DataLocation = paths.DataDirectory;

        OsLine = os.IsWindows11
            ? $"Windows 11 (build {os.BuildNumber})"
            : os.MeetsMinimumOs
                ? $"Windows 10 (build {os.BuildNumber})"
                : $"Unsupported OS (build {os.BuildNumber})";

        FfmpegResolution resolution = ffmpeg.Resolve();
        FfmpegLine = resolution.IsAvailable
            ? $"ffmpeg ready ({resolution.Source}{(resolution.HashVerified ? ", hash-verified" : "")})"
            : $"ffmpeg not found — {resolution.Error?.Message}";

        ThemeLine = $"Theme: {settings.Current.Theme}, accent {settings.Current.Accent}";

        RecoveryLine = crash.PreviousSessionCrashed
            ? "Previous session didn't close cleanly — recovery available."
            : "No pending recovery.";

        StatusPill = "Ready";
    }

    public string Title { get; }
    public string ModeLine { get; }
    public string DataLocation { get; }
    public string OsLine { get; }
    public string FfmpegLine { get; }
    public string ThemeLine { get; }
    public string RecoveryLine { get; }
    public string StatusPill { get; }
}
