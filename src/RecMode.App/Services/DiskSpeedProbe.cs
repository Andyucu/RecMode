using System.IO;

namespace RecMode.App.Services;

/// <summary>
/// Measures write throughput to a folder's volume — the "disk-speed signal" pre-flight check (plan §3.6,
/// user's feature-triage idea list). Complements the free-space check: a network share or an old flash
/// drive can have plenty of free *space* while still being too slow to keep up with a recording.
/// </summary>
public interface IDiskSpeedProbe
{
    /// <summary>Measured sequential write speed in MB/s for <paramref name="directory"/>'s volume, or -1 if the probe couldn't run (best-effort).</summary>
    double MeasureWriteSpeedMBps(string directory);
}

/// <summary>Default <see cref="IDiskSpeedProbe"/>: times writing + flushing a small temp file, bypassing the OS write cache.</summary>
public sealed class DiskSpeedProbe : IDiskSpeedProbe
{
    private const int ProbeSizeBytes = 8 * 1024 * 1024; // 8 MB — enough to smooth out cache/burst noise, fast on any real disk

    public double MeasureWriteSpeedMBps(string directory)
    {
        string path = Path.Combine(directory, $".recmode-diskspeed-{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] chunk = new byte[1024 * 1024]; // 1 MB chunk, written repeatedly
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, chunk.Length, FileOptions.WriteThrough))
            {
                for (int written = 0; written < ProbeSizeBytes; written += chunk.Length)
                {
                    fs.Write(chunk, 0, chunk.Length);
                }
                fs.Flush(flushToDisk: true);
            }
            sw.Stop();

            double seconds = sw.Elapsed.TotalSeconds;
            return seconds > 0 ? ProbeSizeBytes / (1024.0 * 1024.0) / seconds : -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1; // best-effort — a failed probe never blocks a recording
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave the stray temp file; harmless.
            }
        }
    }
}
