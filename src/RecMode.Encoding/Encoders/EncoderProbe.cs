using System.Diagnostics;
using System.Text.Json;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.Encoding.Encoders;

/// <summary>
/// Default <see cref="IEncoderProbe"/>: parses <c>ffmpeg -encoders</c> once and intersects the reported
/// encoder ids with <see cref="EncoderCatalog.All"/>. Every candidate is trial-opened, including software
/// encoders, so VM/no-GPU environments only offer CPU encoders that actually work with the bundled ffmpeg.
/// <para>
/// Startup-speed: a full trial-encode pass spawns up to a dozen real ffmpeg subprocesses. The result is
/// persisted to <see cref="IAppPaths.EncoderCachePath"/>, fingerprinted by the resolved ffmpeg binary's path +
/// size + last-write time (cheap file-system stats, not a hash) plus the machine name — a later launch with
/// the same, unchanged ffmpeg binary on the same machine loads the cached id list straight from disk instead
/// of re-running every trial encode. The machine-name check exists specifically for the portable-USB story
/// (§3.5): the same ffmpeg.exe copied to a different machine must not reuse hardware-encoder results probed
/// against a different GPU. It does not catch a GPU swap/driver change on the *same* machine — narrower than
/// ideal, but covers the actual reported scenario without pulling a DXGI adapter-enumeration dependency into
/// this project. Only the id list is persisted; the full <see cref="EncoderInfo"/> records are always resolved
/// back through <see cref="EncoderCatalog.All"/>, so a future catalog change (new display name, etc.) can
/// never go stale in the cache. Best-effort throughout: any cache read/write failure just falls back to a
/// fresh probe.
/// </para>
/// </summary>
public sealed class EncoderProbe(IFfmpegLocator locator, IErrorReporter errors, IAppPaths paths) : IEncoderProbe
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private IReadOnlyList<EncoderInfo>? _cached;

    internal sealed record DiskCache(string FfmpegPath, long SizeBytes, long WriteTimeTicks, List<string> AvailableIds,
        string MachineName = "");

    /// <summary>True if <paramref name="cache"/> was written for exactly this ffmpeg binary on this machine —
    /// same path, same size, same last-write time, same <see cref="Environment.MachineName"/>. Pure and
    /// side-effect-free so it's directly unit-testable without needing a real ffmpeg.exe on disk (the caller
    /// does the actual file-system stat).</summary>
    internal static bool CacheMatches(DiskCache? cache, string ffmpegPath, long sizeBytes, long writeTimeTicks, string machineName) =>
        cache is not null &&
        string.Equals(cache.FfmpegPath, ffmpegPath, StringComparison.OrdinalIgnoreCase) &&
        cache.SizeBytes == sizeBytes &&
        cache.WriteTimeTicks == writeTimeTicks &&
        string.Equals(cache.MachineName, machineName, StringComparison.OrdinalIgnoreCase);

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

        if (TryLoadDiskCache(ff.FfmpegPath, out List<EncoderInfo> cached))
        {
            return cached;
        }

        try
        {
            string output = RunEncodersList(ff.FfmpegPath);

            // `-encoders` lists every *compiled* encoder, including hardware ones absent on this machine
            // (e.g. h264_nvenc reports present but fails with "Cannot load nvcuda.dll" on an AMD GPU). Some
            // unusual ffmpeg builds can also list a software encoder whose linked runtime is unusable. Gate
            // every encoder behind a fast trial encode, with libx264 treated as the baseline CPU path.
            //
            // Trial-encode only the catalog entries -encoders actually lists (same pre-filter
            // SelectAvailableEncoders applies below) concurrently — each is an independent short-lived ffmpeg
            // subprocess, and this only ever runs on a cache miss (first-ever launch, or right after the
            // ffmpeg binary changes) — but bounded to a small degree of parallelism: consumer NVIDIA/AMD/Intel
            // drivers cap concurrent hardware-encoder sessions (historically 2-3 for NVENC), so opening
            // h264/hevc/av1 on the same vendor's encoder all at once can make a trial that would pass serially
            // fail under the burst, and that false negative would otherwise get persisted to disk and hide a
            // working hardware encoder from every future launch. SelectAvailableEncoders itself stays a plain
            // synchronous lookup against the precomputed results, so its existing deterministic-order contract
            // (and the tests pinning it) are untouched.
            var trialResults = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
            List<EncoderInfo> candidates = EncoderCatalog.All.Where(e => ContainsEncoderId(output, e.FfmpegId)).ToList();
            System.Threading.Tasks.Parallel.ForEach(candidates,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 },
                e => trialResults[e.FfmpegId] = TrialEncode(ff.FfmpegPath, e.FfmpegId));
            // libx264 may be missing from -encoders' output on an odd build yet still trial-open successfully
            // (see SelectAvailableEncoders below) — trial it too so that path isn't left unresolved.
            if (candidates.TrueForAll(e => e.FfmpegId != "libx264"))
            {
                trialResults["libx264"] = TrialEncode(ff.FfmpegPath, "libx264");
            }
            List<EncoderInfo> available = SelectAvailableEncoders(output, id => trialResults.GetValueOrDefault(id));
            if (available.Count == 0)
            {
                errors.Warn("encoder.probe-none-working", "No working video encoders were detected.",
                    "Install a full ffmpeg build with libx264 support or configure a valid ffmpeg override.");
            }
            else
            {
                TrySaveDiskCache(ff.FfmpegPath, available);
            }

            return available;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            errors.Warn("encoder.probe-failed", "Encoder detection failed; recording is disabled until encoders can be probed.", null, ex);
            return [];
        }
    }

    private bool TryLoadDiskCache(string ffmpegPath, out List<EncoderInfo> encoders)
    {
        encoders = [];
        try
        {
            if (!File.Exists(paths.EncoderCachePath))
            {
                return false;
            }

            DiskCache? cache = JsonSerializer.Deserialize<DiskCache>(File.ReadAllText(paths.EncoderCachePath), CacheJsonOptions);
            var info = new FileInfo(ffmpegPath);
            if (!info.Exists || !CacheMatches(cache, ffmpegPath, info.Length, info.LastWriteTimeUtc.Ticks, Environment.MachineName))
            {
                return false; // missing, mismatched, ffmpeg was replaced/updated, or this is a different machine
            }

            // AvailableIds is a non-nullable List<string> on the record, but System.Text.Json happily
            // deserializes an explicit `"AvailableIds": null` (or a hand-edited/corrupt cache missing the
            // property) into a null reference anyway — nullable-reference annotations aren't runtime-enforced
            // by the deserializer. Without the null-coalesce below, that threw a NullReferenceException here,
            // which isn't in this method's catch filter, so it escaped uncaught onto the UI thread the moment
            // the Record screen called through to GetAvailableEncoders().
            encoders = (cache!.AvailableIds ?? [])
                .Select(id => EncoderCatalog.All.FirstOrDefault(e => e.FfmpegId == id))
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();
            return encoders.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false; // a missing/corrupt cache just triggers a fresh probe, same as a first-ever launch
        }
    }

    private void TrySaveDiskCache(string ffmpegPath, List<EncoderInfo> encoders)
    {
        try
        {
            var info = new FileInfo(ffmpegPath);
            var cache = new DiskCache(ffmpegPath, info.Length, info.LastWriteTimeUtc.Ticks,
                encoders.Select(e => e.FfmpegId).ToList(), Environment.MachineName);
            Directory.CreateDirectory(paths.DataDirectory);
            // Same shared atomic write as SettingsService/LibraryIndex - a process kill mid-write must not
            // leave a torn JSON file that TryLoadDiskCache's JsonException catch would otherwise silently
            // treat as "corrupt, re-probe from scratch" on every single future launch.
            AtomicFileWriter.Write(paths.EncoderCachePath, JsonSerializer.Serialize(cache, CacheJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort — losing the cache just means the next launch re-probes from scratch.
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
        // block on a full stderr pipe while stdout is being read).
        var psi = new ProcessStartInfo(ffmpegPath, "-hide_banner -encoders")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg for encoder probe.");

        // Read stdout asynchronously (BeginOutputReadLine) rather than a blocking ReadToEnd() before
        // WaitForExit — the previous ordering meant WaitForExit's 10s timeout could never actually run: a
        // synchronous ReadToEnd() only returns once the child closes its stdout handle (normally: exits),
        // so a hung ffmpeg blocked here forever regardless of the timeout/kill logic below it.
        var stdout = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.BeginOutputReadLine();

        if (process.WaitForExit(10000))
        {
            // Per the WaitForExit(int) docs: with asynchronous redirected output, this overload can return
            // true before the last OutputDataReceived callbacks have actually fired. The parameterless
            // overload blocks until they have, guaranteeing the encoders list below is complete.
            process.WaitForExit();
        }
        else
        {
            // A `using Process` disposes the .NET wrapper object, not the child process it represents — a
            // process that outlives its WaitForExit timeout keeps running (and, for a probe like this,
            // would run forever) unless explicitly killed, orphaning an ffmpeg.exe that outlives RecMode.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }

        return stdout.ToString();
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

            // Drain stderr asynchronously rather than blocking on ReadToEnd() before WaitForExit — same
            // reasoning as RunEncodersList. This was the actual reason the timeout/kill below it could
            // never run for a genuinely hung trial (the documented h264_qsv/bad-Intel-driver case this
            // comment already described): ReadToEnd() only returns once the child exits, so a hang blocked
            // here forever regardless of the 5s timeout that was supposedly guarding it. The content isn't
            // needed here (unlike RunEncodersList's stdout) — just draining it is enough to avoid the classic
            // full-pipe-buffer deadlock, so no handler is attached to WaitForExit for the trailing events.
            process.BeginErrorReadLine();
            if (process.WaitForExit(5000))
            {
                return process.ExitCode == 0;
            }

            // A hung trial (a known failure mode for a broken driver install, e.g. h264_qsv on a machine
            // with a bad Intel driver) must not leave its ffmpeg.exe running past this timeout — `using
            // Process` only disposes the .NET wrapper, not the child. The now-parallel/bounded trial burst
            // (EncoderProbe.Probe) makes a hang here more likely to actually happen than when trials ran
            // one at a time, so this is more than theoretical.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return false;
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
