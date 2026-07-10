using System.Diagnostics;
using RecMode.Core.Errors;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.Encoding.Encoders;

/// <summary>
/// Default <see cref="IEncoderProbe"/>: parses <c>ffmpeg -encoders</c> once and intersects the reported
/// encoder ids with <see cref="EncoderCatalog.All"/>. Every candidate is trial-opened, including software
/// encoders, so VM/no-GPU environments only offer CPU encoders that actually work with the bundled ffmpeg.
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
                "Recording is disabled until ffmpeg is present.");
            return [];
        }

        try
        {
            string output = RunEncodersList(ff.FfmpegPath);

            // `-encoders` lists every *compiled* encoder, including hardware ones absent on this machine
            // (e.g. h264_nvenc reports present but fails with "Cannot load nvcuda.dll" on an AMD GPU). Some
            // unusual ffmpeg builds can also list a software encoder whose linked runtime is unusable. Gate
            // every encoder behind a fast trial encode, with libx264 treated as the baseline CPU path.
            List<EncoderInfo> available = SelectAvailableEncoders(output, id => TrialEncode(ff.FfmpegPath, id));
            if (available.Count == 0)
            {
                errors.Warn("encoder.probe-none-working", "No working video encoders were detected.",
                    "Install a full ffmpeg build with libx264 support or configure a valid ffmpeg override.");
            }

            return available;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            errors.Warn("encoder.probe-failed", "Encoder detection failed; recording is disabled until encoders can be probed.", null, ex);
            return [];
        }
    }

    /// <summary>
    /// Selects encoders from ffmpeg's <c>-encoders</c> output using a supplied trial-open function. Kept public
    /// so tests can lock the no-GPU/software-fallback behavior without launching a real ffmpeg process.
    /// </summary>
    public static List<EncoderInfo> SelectAvailableEncoders(string encodersOutput, Func<string, bool> trialEncode)
    {
        ArgumentNullException.ThrowIfNull(encodersOutput);
        ArgumentNullException.ThrowIfNull(trialEncode);

        var available = new List<EncoderInfo>();
        foreach (EncoderInfo e in EncoderCatalog.All)
        {
            if (ContainsEncoderId(encodersOutput, e.FfmpegId) && trialEncode(e.FfmpegId))
            {
                available.Add(e);
            }
        }

        // A full bundled ffmpeg should expose libx264. If an odd ffmpeg build hides it from -encoders but it
        // still opens successfully, keep the app usable in CPU-only/VM environments.
        EncoderInfo? x264 = EncoderCatalog.All.FirstOrDefault(e => e.FfmpegId == "libx264");
        if (x264 is not null &&
            !available.Exists(e => e.FfmpegId == x264.FfmpegId) &&
            trialEncode(x264.FfmpegId))
        {
            available.Add(x264);
        }

        return available;
    }

    private static string RunEncodersList(string ffmpegPath)
    {
        // Only stdout is redirected — we don't read stderr, and redirecting a stream without ever draining
        // it risks a classic deadlock if ffmpeg writes enough there to fill the OS pipe buffer (it would
        // block on a full stderr pipe while ReadToEnd() below blocks waiting for more stdout/process exit).
        var psi = new ProcessStartInfo(ffmpegPath, "-hide_banner -encoders")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
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

}
