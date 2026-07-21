using System.Collections.Concurrent;

namespace RecMode.Core.Errors;

/// <summary>
/// Default <see cref="IErrorReporter"/>: raises the event and retains a bounded ring of recent errors
/// so a diagnostics view (or a test) can inspect what happened without a logging round-trip.
/// Thread-safe; allocation-light on the report path.
/// </summary>
public sealed class ErrorReporter : IErrorReporter
{
    private const int MaxRetained = 200;
    private readonly ConcurrentQueue<RecModeError> _recent = new();

    public event EventHandler<RecModeError>? ErrorReported;

    /// <summary>Snapshot of recent errors, oldest first.</summary>
    public IReadOnlyList<RecModeError> Recent => _recent.ToArray();

    public void Report(RecModeError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _recent.Enqueue(error);
        while (_recent.Count > MaxRetained && _recent.TryDequeue(out _))
        {
            // Trim to bound.
        }

        ErrorReported?.Invoke(this, error);
    }
}
