namespace RecMode.Core.Settings;

/// <summary>
/// Derives the user-tunable performance ranges from the hardware (plan §3.3 — controls "bounded by
/// hardware-derived recommendations"). Currently the CPU thread-cap options for software encoding. Pure so it's
/// unit-testable; the app feeds it <see cref="Environment.ProcessorCount"/>.
/// </summary>
public static class PerformanceBounds
{
    private static readonly int[] Candidates = [2, 4, 6, 8, 12, 16, 24, 32];

    /// <summary>
    /// Thread-cap choices for a machine with <paramref name="logicalCores"/> logical processors: 0 (= Auto /
    /// all cores) plus the candidate steps that don't exceed the core count, always including the core count
    /// itself. Never offers more threads than the CPU has.
    /// </summary>
    public static IReadOnlyList<int> ThreadCapOptions(int logicalCores)
    {
        int cores = Math.Max(1, logicalCores);
        var options = new List<int> { 0 };

        foreach (int c in Candidates)
        {
            if (c < cores)
            {
                options.Add(c);
            }
        }

        if (cores > 1 && !options.Contains(cores))
        {
            options.Add(cores);
        }

        return options;
    }
}
