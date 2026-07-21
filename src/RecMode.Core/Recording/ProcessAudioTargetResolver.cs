namespace RecMode.Core.Recording;

/// <summary>
/// Resolves a persisted per-app audio target (a process *name*, since PIDs aren't stable across restarts —
/// plan §7 per-app audio) against the currently-running candidates. Pure/testable; the actual process
/// enumeration lives in <c>RecMode.Capture</c> (a real OS call).
/// </summary>
public static class ProcessAudioTargetResolver
{
    /// <summary>
    /// Returns the process ID of the first running candidate whose name matches <paramref name="targetProcessName"/>
    /// (ordinal, case-insensitive), or null if the name is empty/unset or no candidate matches (e.g. the app isn't running).
    /// </summary>
    public static int? Resolve(IEnumerable<(string ProcessName, int ProcessId)> candidates, string? targetProcessName)
    {
        if (string.IsNullOrWhiteSpace(targetProcessName))
        {
            return null;
        }

        foreach ((string processName, int processId) in candidates)
        {
            if (string.Equals(processName, targetProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return processId;
            }
        }

        return null;
    }
}
