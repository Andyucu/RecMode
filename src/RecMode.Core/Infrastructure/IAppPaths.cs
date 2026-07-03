namespace RecMode.Core.Infrastructure;

/// <summary>
/// The single source of truth for every filesystem location RecMode uses (plan §3.5). No other code
/// may compose a state path itself. In portable mode (a <c>portable.marker</c> next to the exe) all
/// state lives under <c>.\Data</c> and recordings default to <c>.\Recordings</c>; in installed mode
/// it resolves to <c>%APPDATA%</c>/<c>%LOCALAPPDATA%</c>.
/// </summary>
public interface IAppPaths
{
    /// <summary>True when running from a portable folder (marker present).</summary>
    bool IsPortable { get; }

    /// <summary>Directory containing RecMode.exe.</summary>
    string AppDirectory { get; }

    /// <summary>Root for mutable state (settings, library index, crash-recovery temp).</summary>
    string DataDirectory { get; }

    /// <summary>Log files.</summary>
    string LogsDirectory { get; }

    /// <summary>Opt-in crash minidumps (§3.6).</summary>
    string CrashDumpDirectory { get; }

    /// <summary>Default output folder for recordings (user-overridable in Settings).</summary>
    string RecordingsDirectory { get; }

    /// <summary>Default output folder for screenshots.</summary>
    string ScreenshotsDirectory { get; }

    /// <summary>Bundled ffmpeg location (<c>.\ffmpeg</c> in portable mode).</summary>
    string FfmpegDirectory { get; }

    /// <summary>Shipped license notices.</summary>
    string LicensesDirectory { get; }

    /// <summary>Full path to the settings JSON file.</summary>
    string SettingsFilePath { get; }

    /// <summary>Full path to the library index file.</summary>
    string LibraryIndexPath { get; }

    /// <summary>Creates the state directories if missing. Call once at startup.</summary>
    void EnsureDirectories();

    /// <summary>
    /// Verifies <see cref="DataDirectory"/> is writable (guards against Program Files / un-extracted zip,
    /// plan §3.5 read-only location guard). Returns false without throwing on failure.
    /// </summary>
    bool IsDataDirectoryWritable();
}
