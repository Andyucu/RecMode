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

    /// <summary>
    /// Converts a user-chosen absolute folder into the form that should be persisted in settings: stored
    /// <em>relative</em> to <see cref="AppDirectory"/> when it lives inside the app folder, absolute
    /// otherwise. This is what keeps the portable promise (§3.5) intact across a move: an output folder
    /// inside the app folder used to be persisted as an absolute path, so copying the portable folder to a
    /// USB stick and running it on another machine re-created the <em>original</em> absolute path there —
    /// writing recordings outside the app folder, onto a borrowed machine's system drive, and leaving them
    /// behind. A genuinely external folder the user picked on purpose stays absolute; that's their explicit
    /// choice, the same category of labelled opt-in as the "Start with Windows" registry entry.
    /// </summary>
    string? ToPortableSetting(string? absolutePath);

    /// <summary>Resolves a value persisted by <see cref="ToPortableSetting"/> back to an absolute path,
    /// re-anchoring a relative one against the <em>current</em> <see cref="AppDirectory"/>.</summary>
    string? ResolveUserPath(string? storedPath);

    /// <summary>Full path to the settings JSON file.</summary>
    string SettingsFilePath { get; }

    /// <summary>Full path to the library index file.</summary>
    string LibraryIndexPath { get; }

    /// <summary>Full path to the encoder-probe cache file (§3.9 startup-speed: avoids re-trial-encoding every
    /// catalog encoder on every launch when the ffmpeg binary hasn't changed).</summary>
    string EncoderCachePath { get; }

    /// <summary>Creates the state directories if missing. Call once at startup.</summary>
    void EnsureDirectories();

    /// <summary>
    /// Verifies <see cref="DataDirectory"/> is writable (guards against Program Files / un-extracted zip,
    /// plan §3.5 read-only location guard). Returns false without throwing on failure.
    /// </summary>
    bool IsDataDirectoryWritable();
}
