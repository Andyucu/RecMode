namespace RecMode.Core.Errors;

/// <summary>
/// Central sink for all <see cref="RecModeError"/> occurrences. Subsystems report here; the UI layer
/// subscribes and routes each severity to its channel (toast / InfoBar / toolbar badge / recovery
/// dialog). Reporting is decoupled from presentation so Core has no UI dependency.
/// </summary>
public interface IErrorReporter
{
    /// <summary>Raised (on the reporting thread) whenever an error is reported. Handlers must marshal to the UI thread themselves.</summary>
    event EventHandler<RecModeError>? ErrorReported;

    void Report(RecModeError error);
}

/// <summary>Convenience extensions mirroring the <see cref="RecModeError"/> factories.</summary>
public static class ErrorReporterExtensions
{
    public static void Warn(this IErrorReporter reporter, string code, string message, string? suggestion = null, Exception? ex = null)
        => reporter.Report(RecModeError.Warning(code, message, suggestion, ex));

    public static void Block(this IErrorReporter reporter, string code, string message, string? suggestion = null, Exception? ex = null)
        => reporter.Report(RecModeError.Blocking(code, message, suggestion, ex));

    public static void Degrade(this IErrorReporter reporter, string code, string message, string? suggestion = null, Exception? ex = null)
        => reporter.Report(RecModeError.Degraded(code, message, suggestion, ex));

    public static void Fatal(this IErrorReporter reporter, string code, string message, string? suggestion = null, Exception? ex = null)
        => reporter.Report(RecModeError.Fatal(code, message, suggestion, ex));
}
