using System.Diagnostics;
using RecMode.Core.Errors;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.Encoding.Encoders;

/// <summary>
/// Default <see cref="IEncoderProbe"/>: parses <c>ffmpeg -encoders</c> once and intersects the reported
/// encoder ids with <see cref="EncoderCatalog.All"/>. Falls back to software-only if ffmpeg can't be run,
/// reporting a warning rather than throwing (so the UI still works with x264).
/// </summary>
public sealed class EncoderProbe(IFfmpegLocator locator, IErrorReporter errors) : IEncoderProbe
{
    private readonly Lock _gate = new();
    private IReadOnlyList<EncoderInfo>? _cached;

    public IReadOnlyList<EncoderInfo> GetAvailableEncoders()
    {
        lock (_gate)
        {
            return _cached ??= Probe();
        }
    }

    private List<EncoderInfo> Probe()
    {
        FfmpegResolution ff = locator.Resolve();
        if (!ff.IsAvailable || ff.FfmpegPath is null)
        {
            errors.Warn("encoder.probe-no-ffmpeg", "Couldn't detect encoders — ffmpeg is unavailable.",
                "Only software encoding will be offered once ffmpeg is present.");
            return SoftwareOnly();
        }

        try
        {
            string output = RunEncodersList(ff.FfmpegPath);

            // `-encoders` lists every *compiled* encoder, including hardware ones absent on this machine
            // (e.g. h264_nvenc reports present but fails with "Cannot load nvcuda.dll" on an AMD GPU). So
            // gate hardware encoders behind a fast trial-encode (plan §3.2). Software is always available.
            var available = new List<EncoderInfo>();
            foreach (EncoderInfo e in EncoderCatalog.All)
            {
                if (!ContainsEncoderId(output, e.FfmpegId))
                {
                    continue;
                }

                if (!e.IsHardware || TrialEncode(ff.FfmpegPath, e.FfmpegId))
                {
                    available.Add(e);
                }
            }

            return available.Count == 0 ? SoftwareOnly() : available;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            errors.Warn("encoder.probe-failed", "Encoder detection failed; defaulting to software.", null, ex);
            return SoftwareOnly();
        }
    }

    private static string RunEncodersList(string ffmpegPath)
    {
        var psi = new ProcessStartInfo(ffmpegPath, "-hide_banner -encoders")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg for encoder probe.");
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);
        return stdout;
    }

    /// <summary>A ~0.2s trial encode of a black frame; returns true only if the encoder actually opens on this hardware.</summary>
    private static bool TrialEncode(string ffmpegPath, string encoderId)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpegPath,
                $"-hide_banner -loglevel error -f lavfi -i color=c=black:s=256x144:r=30 -frames:v 2 -c:v {encoderId} -f null -")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.StandardError.ReadToEnd();
            return process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    private static bool ContainsEncoderId(string encodersOutput, string id)
    {
        // Lines look like " V....D h264_amf   AMD AMF H.264 Encoder". Match the id as a whole token.
        foreach (ReadOnlySpan<char> line in encodersOutput.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> trimmed = line.TrimStart();
            int space = trimmed.IndexOf(' ');
            if (space <= 0)
            {
                continue;
            }

            ReadOnlySpan<char> rest = trimmed[(space + 1)..].TrimStart();
            int tokenEnd = rest.IndexOf(' ');
            ReadOnlySpan<char> token = tokenEnd < 0 ? rest : rest[..tokenEnd];
            if (token.SequenceEqual(id))
            {
                return true;
            }
        }

        return false;
    }

    private static List<EncoderInfo> SoftwareOnly() =>
        EncoderCatalog.All.Where(e => e.Backend is Core.Settings.EncoderBackend.Software).ToList();
}
