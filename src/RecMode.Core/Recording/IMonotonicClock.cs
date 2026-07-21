using System.Diagnostics;

namespace RecMode.Core.Recording;

/// <summary>
/// A monotonic, non-decreasing clock (the plan's "shared QPC master clock", §3.3/§3.7). Injectable so
/// state-machine and pacing tests can drive time deterministically instead of sleeping.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>Time elapsed since some fixed, arbitrary origin. Only differences are meaningful.</summary>
    TimeSpan Elapsed { get; }
}

/// <summary>Production clock backed by <see cref="Stopwatch"/> (QPC on Windows).</summary>
public sealed class StopwatchClock : IMonotonicClock
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    public TimeSpan Elapsed => _sw.Elapsed;
}

/// <summary>Test clock whose time only advances when the test moves it. Not thread-safe by design.</summary>
public sealed class ManualClock(TimeSpan start = default) : IMonotonicClock
{
    public TimeSpan Elapsed { get; private set; } = start;

    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(by), "A monotonic clock cannot move backwards.");
        }

        Elapsed += by;
    }
}
