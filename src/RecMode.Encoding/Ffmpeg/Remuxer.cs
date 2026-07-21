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

        try
        {
            var psi = new ProcessStartInfo(ffmpegPath,
                $"-hide_banner -loglevel error -i \"{sourcePath}\" -c copy -movflags +faststart -y \"{mp4Path}\"")
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

            return process.ExitCode == 0 && File.Exists(mp4Path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}
