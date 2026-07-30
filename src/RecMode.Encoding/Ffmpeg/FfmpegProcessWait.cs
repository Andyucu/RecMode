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
    public static bool WaitWithStallDetection(Process process, string? progressFilePath, TimeSpan stallTimeout, out int exitCode)
    {
        TimeSpan lastCpu = TimeSpan.Zero;
        long lastSize = -1;
        Stopwatch sinceProgress = Stopwatch.StartNew();

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
                catch (IOException)
                {
                }
            }

            if (cpu > lastCpu || size != lastSize)
            {
                lastCpu = cpu;
                lastSize = size;
                sinceProgress.Restart();
            }
            else if (sinceProgress.Elapsed > stallTimeout)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.WaitForExit(2000);
                exitCode = -1;
                return false;
            }
        }
    }
}
