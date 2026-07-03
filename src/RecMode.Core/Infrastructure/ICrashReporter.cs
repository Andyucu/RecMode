namespace RecMode.Core.Infrastructure;

/// <summary>
/// App-level crash safety (plan §3.6). A "session open" sentinel is written at startup and removed on a
/// clean exit; if it survives to the next launch, the previous session crashed and recovery is offered.
/// Unhandled exceptions are logged and, when the user opted in, a minidump is written locally.
/// </summary>
public interface ICrashReporter
{
    /// <summary>Whether the *previous* session ended without a clean shutdown (captured at startup).</summary>
    bool PreviousSessionCrashed { get; }

    /// <summary>Writes the session-open sentinel. Call once at startup, after paths exist.</summary>
    void MarkSessionStarted();

    /// <summary>Removes the sentinel to signal a clean shutdown. Call on normal exit.</summary>
    void MarkSessionEndedCleanly();

    /// <summary>Logs an unhandled exception and, if enabled, writes a minidump. Safe to call from a crashing thread.</summary>
    void RecordUnhandledException(Exception exception, bool isTerminating);
}
