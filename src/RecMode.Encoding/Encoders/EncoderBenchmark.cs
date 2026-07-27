using System.Diagnostics;

namespace RecMode.Encoding.Encoders;

/// <summary>One benchmarked encoder's measured throughput.</summary>
public sealed record EncoderBenchmarkResult(EncoderInfo Encoder, double FramesPerSecond);

/// <summary>
/// First-run encoder benchmark → recommended defaults (plan Tier-5 backlog). Unlike <see cref="EncoderProbe"/>'s
/// trial-encode (which only answers "does this encoder open at all"), this measures actual throughput with a
/// short real encode of synthetic 720p30 content, then recommends the fastest encoder capable of real-time
/// 60fps (with headroom) — preferring hardware, since that's the plan's own §3.9 default when present, and
/// falling back to the fastest software encoder otherwise. Runs once, on first launch only (see
/// <c>App.xaml.cs</c>), entirely in the background so it never delays the Record screen becoming usable.
/// </summary>
public static class EncoderBenchmark
{
    private const int TestWidth = 1280, TestHeight = 720, TestFps = 30, TestFrames = 60; // ~2s of synthetic content
    private const double RealtimeTargetFps = 60.0; // recommend an encoder with headroom above a common target rate

    /// <summary>Benchmarks each of <paramref name="candidates"/> (already known to work, e.g. from
    /// <see cref="IEncoderProbe.GetAvailableEncoders"/>) and returns the recommended one, or null if none could
    /// be benchmarked (falls back to the existing default-picking logic).</summary>
    /// <param name="shouldAbort">Polled before each candidate. Benchmarking opens a real hardware encoder
    /// session per candidate, and consumer NVENC/AMF/QSV drivers cap concurrent sessions — so if the user
    /// starts a recording while this is still running, the recording is the one that loses and falls back to
    /// software. Returning true abandons the run and yields no recommendation (partial results would be
    /// biased toward whichever encoders happened to finish first).</param>
    public static EncoderInfo? Recommend(string ffmpegPath, IReadOnlyList<EncoderInfo> candidates,
        Func<bool>? shouldAbort = null) =>
        SelectRecommended(Run(ffmpegPath, candidates, shouldAbort));

    /// <summary>The pure selection rule, split out from <see cref="Recommend"/> so it's directly unit-testable
    /// against synthetic results — shelling out to real ffmpeg (<see cref="Run"/>) isn't something a unit test
    /// can exercise. Prefers the fastest hardware encoder with real-time headroom (§3.9's own default when
    /// present), falling back to the fastest of whatever was actually benchmarked.</summary>
    internal static EncoderInfo? SelectRecommended(IReadOnlyList<EncoderBenchmarkResult> results)
    {
        if (results.Count == 0)
        {
            return null;
        }

        EncoderBenchmarkResult? bestHardware = results
            .Where(r => r.Encoder.IsHardware && r.FramesPerSecond >= RealtimeTargetFps)
            .OrderByDescending(r => r.FramesPerSecond)
            .FirstOrDefault();
        if (bestHardware is not null)
        {
            return bestHardware.Encoder;
        }

        return results.OrderByDescending(r => r.FramesPerSecond).First().Encoder;
    }

    /// <summary>Runs the actual timed encodes via real ffmpeg subprocesses.</summary>
    internal static List<EncoderBenchmarkResult> Run(string ffmpegPath, IReadOnlyList<EncoderInfo> candidates,
        Func<bool>? shouldAbort = null)
    {
        var results = new List<EncoderBenchmarkResult>();
        foreach (EncoderInfo encoder in candidates)
        {
            if (shouldAbort?.Invoke() == true)
            {
                return []; // see Recommend's shouldAbort docs — discard rather than recommend from a partial run
            }

            if (TryBenchmarkOne(ffmpegPath, encoder.FfmpegId, out double fps))
            {
                results.Add(new EncoderBenchmarkResult(encoder, fps));
            }
        }
        return results;
    }

    private static bool TryBenchmarkOne(string ffmpegPath, string encoderId, out double framesPerSecond)
    {
        framesPerSecond = 0;
        try
        {
            var psi = new ProcessStartInfo(ffmpegPath,
                $"-hide_banner -loglevel error -f lavfi -i testsrc=size={TestWidth}x{TestHeight}:rate={TestFps} " +
                $"-frames:v {TestFrames} -c:v {encoderId} -f null -")
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

            // stderr is redirected, so it must actually be drained: a hardware encoder that fails verbosely
            // (the documented h264_qsv/bad-driver case) fills the pipe buffer, blocks ffmpeg's write, and
            // never exits — the 10s timeout below then scores it "unbenchmarkable" and drops it, so the
            // recommendation can land on a genuinely slower encoder. Same fix already applied in EncoderProbe.
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            long start = Stopwatch.GetTimestamp();
            bool exited = process.WaitForExit(10_000);
            long elapsedTicks = Stopwatch.GetTimestamp() - start;

            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return false;
            }
            if (process.ExitCode != 0)
            {
                return false;
            }

            double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
            if (elapsedSeconds <= 0)
            {
                return false;
            }

            framesPerSecond = TestFrames / elapsedSeconds;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
