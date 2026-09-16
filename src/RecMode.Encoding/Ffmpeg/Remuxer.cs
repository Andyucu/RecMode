using System.Diagnostics;
using System.IO;
using RecMode.Core.Settings;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>
/// Stream-copy remuxes (no re-encode): the crash-safe MKV → MP4/MOV finalize (`-c copy -movflags +faststart`),
/// and writing chapters into an already-finished file from an ffmetadata input. Shared by the safe-recording
/// stop path, the launch-time orphan recovery (plan §3) and the chapter-marker post-pass (plan §7).
/// </summary>
public static class Remuxer
{
    /// <summary>Remuxes <paramref name="sourcePath"/> → <paramref name="mp4Path"/>. Returns true on success
    /// (exit 0 + output exists). <paramref name="sourceCodec"/>, when known, adds <c>-tag:v hvc1</c> for HEVC —
    /// MKV carries no MP4-style codec tag to preserve via <c>-c copy</c>, so an HEVC stream remuxed without it
    /// gets ffmpeg's default <c>hev1</c> tag, which several real players (QuickTime, iOS, Safari, Windows
    /// Photos' HEVC path) refuse to play in an MP4/MOV container even though the bitstream itself is fine.
    /// Null (the orphan-recovery caller, which doesn't know a since-crashed session's codec without probing the
    /// file) preserves the previous behavior.</summary>
    /// <summary><paramref name="timeoutMs"/> is a no-progress stall window, not a total budget — see
    /// <see cref="FfmpegProcessWait"/>. A 1-hour recording's remux can be tens of GB of I/O and legitimately
    /// take much longer than any fixed total budget; a genuinely hung ffmpeg is still caught this fast.</summary>
    /// <summary><paramref name="chapters"/>, when non-empty, is written into the output as real seekable
    /// chapters via an ffmetadata input + <c>-map_metadata 1</c> — riding along with the remux this pass was
    /// already doing, so it costs no extra processing. Verified in both MP4 and MKV against the bundled
    /// ffmpeg.</summary>
    public static bool RemuxToMp4(string ffmpegPath, string sourcePath, string mp4Path, VideoCodec? sourceCodec = null,
        int timeoutMs = 30000, IReadOnlyList<ChapterMark>? chapters = null)
    {
        if (!File.Exists(ffmpegPath) || !File.Exists(sourcePath))
        {
            return false;
        }

        string directory = Path.GetDirectoryName(mp4Path) ?? ".";
        string? temporaryPath = Path.Combine(directory,
            $".{Path.GetFileNameWithoutExtension(mp4Path)}.remux-{Guid.NewGuid():N}{Path.GetExtension(mp4Path)}");
        string? chapterFile = chapters is { Count: > 0 } ? ChapterMetadata.WriteTempFile(chapters, directory) : null;
        string tag = sourceCodec == VideoCodec.Hevc ? "-tag:v hvc1 " : "";

        try
        {
            if (!RunRemux(ffmpegPath, sourcePath, temporaryPath, $"-map 0:v:0 -map 0:a? -c copy {tag}-movflags +faststart",
                    chapterFile, timeoutMs))
            {
                return false;
            }

            // A completed remux is only made visible at its final path after it has succeeded. This keeps
            // interrupted conversions from leaving a plausible-looking but corrupt recording beside the MKV.
            File.Move(temporaryPath, mp4Path, overwrite: false);
            temporaryPath = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(chapterFile);
        }
    }

    /// <summary>Writes chapters into an already-finished recording by stream-copying it once (no re-encode),
    /// replacing the original only after the new file is complete. Needed for containers where no remux
    /// happens otherwise — a direct MKV recording, where chapters can't be muxed inline because they aren't
    /// known until the recording ends.</summary>
    public static bool ApplyChapters(string ffmpegPath, string filePath, IReadOnlyList<ChapterMark> chapters, int timeoutMs = 60000)
    {
        if (!File.Exists(ffmpegPath) || !File.Exists(filePath) || chapters.Count == 0)
        {
            return false;
        }

        string directory = Path.GetDirectoryName(filePath) ?? ".";
        string? temporaryPath = Path.Combine(directory,
            $".{Path.GetFileNameWithoutExtension(filePath)}.chapters-{Guid.NewGuid():N}{Path.GetExtension(filePath)}");
        string? chapterFile = ChapterMetadata.WriteTempFile(chapters, directory);

        try
        {
            if (!RunRemux(ffmpegPath, filePath, temporaryPath, "-map 0:v:0 -map 0:a? -c copy", chapterFile, timeoutMs))
            {
                return false;
            }

            File.Move(temporaryPath, filePath, overwrite: true);
            temporaryPath = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(chapterFile);
        }
    }

    private static bool RunRemux(string ffmpegPath, string sourcePath, string destinationPath, string mapsAndCodec,
        string? chapterFile, int timeoutMs)
    {
        // The chapter document is a second input; -map_metadata 1 then takes its chapter table. Everything
        // else is still stream-copied from input 0.
        string chaptersInput = chapterFile is null ? "" : $"-i \"{chapterFile}\" ";
        string metadataMap = chapterFile is null ? "" : "-map_metadata 1 ";

        var psi = new ProcessStartInfo(ffmpegPath,
            $"-hide_banner -loglevel error -i \"{sourcePath}\" {chaptersInput}{mapsAndCodec} {metadataMap}-y \"{destinationPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        using Process? process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        // Drain stderr asynchronously rather than ReadToEnd() (which has no timeout of its own — if
        // ffmpeg ever hung without exiting, that call would block forever regardless of the timeout,
        // freezing the caller; these remuxes run synchronously on the UI thread via Stop() → Finalize()).
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();

        if (!FfmpegProcessWait.WaitWithStallDetection(process, destinationPath, TimeSpan.FromMilliseconds(timeoutMs), out int exitCode))
        {
            return false;
        }

        return exitCode == 0 && File.Exists(destinationPath);
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a leftover temp file is harmless (and the orphan scan ignores dotfiles).
        }
    }
}
