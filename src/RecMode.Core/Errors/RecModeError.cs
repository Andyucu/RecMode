namespace RecMode.Core.Errors;

/// <summary>
/// A single failure occurrence, carrying its severity, a stable code, a human message, an optional
/// suggested fix, and the originating exception (if any). Immutable value object — construct via the
/// static factories so the severity is always explicit at the call site.
/// </summary>
public sealed record RecModeError
{
    /// <summary>Stable, machine-readable code (e.g. <c>encoder.init-failed</c>) for logging/telemetry-free correlation.</summary>
    public required string Code { get; init; }

    public required ErrorSeverity Severity { get; init; }

    /// <summary>User-facing, sentence-case message. No trailing period control here — UI decides.</summary>
    public required string Message { get; init; }

    /// <summary>Optional suggested next step shown alongside the message (e.g. "Choose a different folder").</summary>
    public string? Suggestion { get; init; }

    /// <summary>Originating exception, if this error wraps one. Not shown raw to users.</summary>
    public Exception? Exception { get; init; }

    /// <summary>When the error occurred (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public static RecModeError Warning(string code, string message, string? suggestion = null, Exception? ex = null) =>
        new() { Code = code, Severity = ErrorSeverity.RecoverableWarning, Message = message, Suggestion = suggestion, Exception = ex };

    public static RecModeError Blocking(string code, string message, string? suggestion = null, Exception? ex = null) =>
        new() { Code = code, Severity = ErrorSeverity.BlockingError, Message = message, Suggestion = suggestion, Exception = ex };

    public static RecModeError Degraded(string code, string message, string? suggestion = null, Exception? ex = null) =>
        new() { Code = code, Severity = ErrorSeverity.DegradedState, Message = message, Suggestion = suggestion, Exception = ex };

    public static RecModeError Fatal(string code, string message, string? suggestion = null, Exception? ex = null) =>
        new() { Code = code, Severity = ErrorSeverity.FatalFinalizationError, Message = message, Suggestion = suggestion, Exception = ex };

    public override string ToString() =>
        $"[{Severity}] {Code}: {Message}{(Suggestion is null ? "" : $" — {Suggestion}")}";
}
