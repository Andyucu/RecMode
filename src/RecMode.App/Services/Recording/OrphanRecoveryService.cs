using System.IO;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Encoding.Ffmpeg;
using Serilog;

namespace RecMode.App.Services;

/// <summary>
/// Recovers recordings orphaned by a crash (plan §3 safe recording): a session that died mid-recording leaves
/// a crash-safe <c>*.recording.mkv</c> behind. On launch this scans the recordings folder and remuxes each
/// orphan to a playable MP4 (`-c copy`), then removes the temp — so a crash costs at most the final second,
/// never the whole take. Runs off the UI thread; failures keep the MKV and surface a warning.
/// </summary>
public sealed class OrphanRecoveryService(IFfmpegLocator ffmpeg, IAppPaths paths, IErrorReporter errors)
{
    private const string OrphanSuffix = ".recording.mkv";

    /// <summary>Recovers any orphaned recordings found in the recordings folder. Safe to call once at startup.</summary>
    public void RecoverOrphans()
    {
        string dir = paths.RecordingsDirectory;
        if (!Directory.Exists(dir))
        {
            return;
        }

        string[] orphans;
        try
        {
            orphans = Directory.GetFiles(dir, "*" + OrphanSuffix, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not scan for orphaned recordings");
            return;
        }

        if (orphans.Length == 0)
        {
            return;
        }

        FfmpegResolution ff = ffmpeg.Resolve();
        if (!ff.IsAvailable || ff.FfmpegPath is null)
        {
            Log.Warning("Found {Count} orphaned recording(s) but ffmpeg is unavailable to recover them", orphans.Length);
            return;
        }

        int recovered = 0;
        foreach (string orphan in orphans)
        {
            // Guard against a race with a recording that just started (its temp is a live *.recording.mkv):
            // if the file is locked for writing, it isn't an orphan — skip it.
            if (IsInUse(orphan))
            {
                Log.Information("Skipping {Orphan} — it's locked (a recording is in progress)", Path.GetFileName(orphan));
                continue;
            }

            string mp4 = UniqueMp4Path(orphan);
            if (Remuxer.RemuxToMp4(ff.FfmpegPath, orphan, mp4))
            {
                TryDelete(orphan);
                recovered++;
                Log.Information("Recovered orphaned recording {Orphan} -> {Mp4}", Path.GetFileName(orphan), Path.GetFileName(mp4));
            }
            else
            {
                Log.Warning("Could not recover orphaned recording {Orphan}; leaving the MKV in place", Path.GetFileName(orphan));
            }
        }

        if (recovered > 0)
        {
            errors.Warn("recovery.recovered",
                recovered == 1
                    ? "Recovered a recording from a previous session that ended unexpectedly."
                    : $"Recovered {recovered} recordings from a previous session that ended unexpectedly.");
        }
    }

    /// <summary>True if the file can't be opened for exclusive read — i.e. another process (a live recording) holds it.</summary>
    private static bool IsInUse(string path)
    {
        try
        {
            using FileStream _ = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Maps <c>clip.recording.mkv</c> → <c>clip.mp4</c>, avoiding clobbering an existing file. Internal
    /// (rather than private) so it's directly unit-testable against a real scratch directory.</summary>
    internal static string UniqueMp4Path(string orphanPath)
    {
        string dir = Path.GetDirectoryName(orphanPath) ?? "";
        string name = Path.GetFileName(orphanPath);
        string stem = name[..^OrphanSuffix.Length]; // strip ".recording.mkv"
        string candidate = Path.Combine(dir, stem + ".mp4");

        int n = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{stem} (recovered {n}).mp4");
            n++;
        }

        return candidate;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Recovered but could not delete the temporary {Path}", path);
        }
    }
}
