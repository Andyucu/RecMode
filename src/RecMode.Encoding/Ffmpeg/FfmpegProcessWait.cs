using System.Diagnostics;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>
/// Waits for an ffmpeg subprocess to exit by watching for real progress instead of a fixed wall-clock
/// budget. <c>-movflags +faststart</c> makes ffmpeg rewrite the whole output file on EOF (moving <c>moov</c>
/// ahead of <c>mdat</c>) — for a multi-GB recording that pass alone can take far longer than any fixed
/// timeout a caller might pick, especially on a slow/external/network drive. A fixed budget that kills the
/// process mid-rewrite destroys the file (no <c>moov</c>, unplayable) even though ffmpeg was working the
/// whole time. This instead kills ffmpeg only after it stops making progress — no CPU time consumed and no
/// growth in the file it's writing — for longer than <paramref name="stallTimeout"/>, which still catches a
/// genuinely hung/deadlocked process just as fast as a fixed timeout would.
/// </summary>
internal static class FfmpegProcessWait
{
    // A backstop, not the primary mechanism: a process that keeps nudging its CPU time or output file size
    // just often enough to reset the stall clock — an encoder-side busy loop, or a hung network write that
    // still burns CPU — would otherwise never be caught by stall detection alone, since "progress" per the
    // check above is technically always true. Without SOME ceiling, that hangs Stop()'s synchronous
    // Finalize() call forever, and since App.OnExit calls Stop() too, the whole app becomes unquittable and
    // has to be killed from Task Manager — precisely the failure class this file exists to fix, just
    // reintroduced via a different door. Deliberately generous (minutes, not the old 20s/30s) and scaled to
    // how much data has actually been written, so it only ever fires on a genuinely pathological process,
    // never on an honestly slow but real remux/finalize.
    private static readonly TimeSpan AbsoluteCeilingBase = TimeSpan.FromMinutes(2);
    private const double AbsoluteCeilingSecondsPerGiB = 10.0;

    public static bool WaitWithStallDetection(Process process, string? progressFilePath, TimeSpan stallTimeout, out int exitCode)
    {
        TimeSpan lastCpu = TimeSpan.Zero;
        long lastSize = -1;
        long maxSizeSeen = 0;
        Stopwatch sinceProgress = Stopwatch.StartNew();
        Stopwatch overall = Stopwatch.StartNew();

        while (true)
        {
            if (process.WaitForExit(500))
            {
                exitCode = process.ExitCode;
                return true;
            }

            TimeSpan cpu = lastCpu;
            try
            {
                process.Refresh();
                cpu = process.TotalProcessorTime;
            }
            catch (InvalidOperationException)
            {
                // Process exited between WaitForExit(500) returning false and Refresh(); the next
                // WaitForExit will observe it.
            }

            long size = lastSize;
            if (progressFilePath is not null)
            {
                try
                {
                    size = File.Exists(progressFilePath) ? new FileInfo(progressFilePath).Length : -1;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A transient ACL/lock hiccup reading our own output file (seen on some network shares)
                    // must not itself abort an otherwise-healthy finalize — treat it as "no new size sample
                    // this tick" rather than letting the exception escape into the caller's Finalize().
                }
            }

            if (size > maxSizeSeen)
            {
                maxSizeSeen = size;
            }

            if (cpu > lastCpu || size != lastSize)
            {
                lastCpu = cpu;
                lastSize = size;
                sinceProgress.Restart();
            }
            else if (sinceProgress.Elapsed > stallTimeout)
            {
                // Broad by design: the process is being abandoned either way, so this is best-effort cleanup.
                // Process.Kill also throws Win32Exception (access denied, or the process exiting concurrently
                // so the handle op fails) — which used to escape all the way out to Finalize() and turn a
                // merely-stalled ffmpeg into a Fatal "recording couldn't be finalized".
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                process.WaitForExit(2000);
                exitCode = -1;
                return false;
            }

            TimeSpan ceiling = AbsoluteCeilingBase +
                TimeSpan.FromSeconds(maxSizeSeen / (1024.0 * 1024 * 1024) * AbsoluteCeilingSecondsPerGiB);
            if (overall.Elapsed > ceiling)
            {
                // Broad by design: the process is being abandoned either way, so this is best-effort cleanup.
                // Process.Kill also throws Win32Exception (access denied, or the process exiting concurrently
                // so the handle op fails) — which used to escape all the way out to Finalize() and turn a
                // merely-stalled ffmpeg into a Fatal "recording couldn't be finalized".
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                process.WaitForExit(2000);
                exitCode = -1;
                return false;
            }
        }
    }
}
