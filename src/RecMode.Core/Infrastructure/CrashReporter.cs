using System.Globalization;
using System.Linq;

namespace RecMode.Core.Infrastructure;

/// <summary>Default <see cref="ICrashReporter"/>: a sentinel file plus a crash log under the logs dir.</summary>
public sealed class CrashReporter : ICrashReporter
{
    private const string SessionMarkerName = "session.open";

    // Crash logs are tiny but minidumps are not (§3.6's "with data segments" dump is well beyond a triage-sized
    // mini dump); with no pruning at all, a machine that crashes repeatedly over months would accumulate them
    // forever in Data\logs\crash with nothing to stop it. Kept generous since these are the one artifact that
    // actually explains a crash after the fact.
    private const int MaxKeptCrashArtifacts = 20;

    private readonly IAppPaths _paths;
    private readonly IMinidumpWriter _minidump;
    private readonly Func<bool> _minidumpsEnabled;
    private readonly string _markerPath;

    public CrashReporter(IAppPaths paths, IMinidumpWriter minidump, Func<bool> minidumpsEnabled)
    {
        _paths = paths;
        _minidump = minidump;
        _minidumpsEnabled = minidumpsEnabled;
        _markerPath = Path.Combine(paths.CrashDumpDirectory, SessionMarkerName);

        // Capture the *previous* run's state before we overwrite the marker this session.
        PreviousSessionCrashed = File.Exists(_markerPath);
    }

    public bool PreviousSessionCrashed { get; }

    public void MarkSessionStarted()
    {
        try
        {
            Directory.CreateDirectory(_paths.CrashDumpDirectory);
            File.WriteAllText(
                _markerPath,
                $"pid={Environment.ProcessId} started={DateTimeOffset.UtcNow:O}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: crash detection is best-effort.
        }

        PruneOldCrashArtifacts();
    }

    /// <summary>Keeps only the newest <see cref="MaxKeptCrashArtifacts"/> crash-*.log/crash-*.dmp files, oldest
    /// first. Runs once per launch rather than after every crash, so a single session that somehow produced
    /// many crash reports doesn't churn through deletes mid-run.</summary>
    private void PruneOldCrashArtifacts()
    {
        try
        {
            if (!Directory.Exists(_paths.CrashDumpDirectory))
            {
                return;
            }

            IEnumerable<string> logs = Directory.EnumerateFiles(_paths.CrashDumpDirectory, "crash-*.log");
            IEnumerable<string> dumps = Directory.EnumerateFiles(_paths.CrashDumpDirectory, "crash-*.dmp");

            foreach (string path in logs.Concat(dumps)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(MaxKeptCrashArtifacts)
                .Select(f => f.FullName))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: pruning failure shouldn't block startup.
        }
    }

    public void MarkSessionEndedCleanly()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal.
        }
    }

    public void RecordUnhandledException(Exception exception, bool isTerminating)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            Directory.CreateDirectory(_paths.CrashDumpDirectory);
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string baseName = $"crash-{stamp}-{Environment.ProcessId}";
            string logPath = UniqueArtifactPath(baseName, ".log");
            File.WriteAllText(
                logPath,
                $"time={DateTimeOffset.UtcNow:O}\r\nterminating={isTerminating}\r\n\r\n{exception}");

            // _minidumpsEnabled resolves ISettingsService from the DI container (Composition.cs) — if a
            // crash is being reported during/after host teardown (e.g. an exception on the dispatcher while
            // the app is shutting down), that container is already disposed. This must never itself throw:
            // an exception escaping the crash reporter would replace the very exception it's trying to
            // record and, for the DispatcherUnhandledException caller, skip `e.Handled = true` — turning a
            // recoverable error into a fatal one. So this inner call gets its own catch-all, separate from
            // the outer one, rather than widening the outer catch and risking swallowing a real bug in the
            // file-write path above.
            try
            {
                if (_minidumpsEnabled())
                {
                    string dumpPath = UniqueArtifactPath(baseName, ".dmp");
                    _minidump.TryWrite(dumpPath);
                }
            }
            catch (Exception)
            {
                // Best-effort: the crash log above already captured the real exception; a minidump is a bonus.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // We are already handling a crash; swallow secondary IO failures.
        }
    }

    private string UniqueArtifactPath(string baseName, string extension)
    {
        string path = Path.Combine(_paths.CrashDumpDirectory, baseName + extension);
        if (!File.Exists(path)) return path;

        for (int suffix = 1; suffix < 1000; suffix++)
        {
            path = Path.Combine(_paths.CrashDumpDirectory, $"{baseName}-{suffix}{extension}");
            if (!File.Exists(path)) return path;
        }

        // Extremely unlikely; retain uniqueness even if the directory is unusually busy.
        return Path.Combine(_paths.CrashDumpDirectory, $"{baseName}-{Guid.NewGuid():N}{extension}");
    }
}
