using System.Globalization;

namespace RecMode.Core.Infrastructure;

/// <summary>Default <see cref="ICrashReporter"/>: a sentinel file plus a crash log under the logs dir.</summary>
public sealed class CrashReporter : ICrashReporter
{
    private const string SessionMarkerName = "session.open";

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
            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string logPath = Path.Combine(_paths.CrashDumpDirectory, $"crash-{stamp}.log");
            File.WriteAllText(
                logPath,
                $"time={DateTimeOffset.UtcNow:O}\r\nterminating={isTerminating}\r\n\r\n{exception}");

            if (_minidumpsEnabled())
            {
                string dumpPath = Path.Combine(_paths.CrashDumpDirectory, $"crash-{stamp}.dmp");
                _minidump.TryWrite(dumpPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // We are already handling a crash; swallow secondary IO failures.
        }
    }
}
