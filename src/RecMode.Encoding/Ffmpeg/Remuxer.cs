using System.Diagnostics;
using System.IO;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>
/// Stream-copy remux (no re-encode) of a crash-safe MKV into MP4 (`-c copy -movflags +faststart`). Shared by
/// the safe-recording stop path and the launch-time orphan recovery (plan §3 safe recording).
/// </summary>
public static class Remuxer
{
    /// <summary>Remuxes <paramref name="sourcePath"/> → <paramref name="mp4Path"/>. Returns true on success (exit 0 + output exists).</summary>
    public static bool RemuxToMp4(string ffmpegPath, string sourcePath, string mp4Path, int timeoutMs = 30000)
    {
        if (!File.Exists(ffmpegPath) || !File.Exists(sourcePath))
        {
            return false;
        }

        string? temporaryPath = null;
        try
        {
            string directory = Path.GetDirectoryName(mp4Path) ?? ".";
            string extension = Path.GetExtension(mp4Path);
            string stem = Path.GetFileNameWithoutExtension(mp4Path);
            temporaryPath = Path.Combine(directory, $".{stem}.remux-{Guid.NewGuid():N}{extension}");
            var psi = new ProcessStartInfo(ffmpegPath,
                $"-hide_banner -loglevel error -i \"{sourcePath}\" -c copy -movflags +faststart -y \"{temporaryPath}\"")
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
            // ffmpeg ever hung without exiting, that call would block forever regardless of timeoutMs,
            // freezing the caller; RemuxToMp4 runs synchronously on the UI thread via Stop() → Finalize()).
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return false;
            }

            if (process.ExitCode != 0 || !File.Exists(temporaryPath))
            {
                return false;
            }

            // A completed remux is only made visible at its final path after it has succeeded. This keeps
            // interrupted conversions from leaving a plausible-looking but corrupt recording beside the MKV.
            File.Move(temporaryPath, mp4Path, overwrite: false);
            temporaryPath = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
