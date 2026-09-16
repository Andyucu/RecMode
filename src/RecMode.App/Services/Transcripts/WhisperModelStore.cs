using System.IO;
using System.Net.Http;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using Serilog;

namespace RecMode.App.Services.Transcripts;

/// <summary>
/// Owns the speech-to-text model files. These are the one thing RecMode deliberately does NOT ship: 75–466 MB
/// is far too large for a portable zip (plan §3.5), so the model is an **opt-in download** into
/// <c>Data/models/</c> — documented in the UI before the user agrees to it, and never fetched as a side
/// effect of anything else. Everything else about transcription stays local: nothing is uploaded, ever.
/// </summary>
public interface IWhisperModelStore
{
    bool IsAvailable(TranscriptModelSize size);
    string PathFor(TranscriptModelSize size);
    long ExpectedBytes(TranscriptModelSize size);
    string DisplayName(TranscriptModelSize size);
    Task DownloadAsync(TranscriptModelSize size, IProgress<double>? progress, CancellationToken ct);
}

/// <summary>Default <see cref="IWhisperModelStore"/> — plain HTTPS download with a partial-file rename, so an
/// interrupted download can never be mistaken for a usable model.</summary>
public sealed class WhisperModelStore : IWhisperModelStore
{
    /// <summary>Exact repository revision the hashes below were taken from. Updating a model means updating
    /// this and the matching <see cref="ExpectedSha256"/> entry together, deliberately — never automatically.</summary>
    private const string PinnedRevision = "5359861c739e955e79d9a303bcbc70fb988958b1";

    /// <summary>No total timeout: a 490 MB model on a slow line legitimately takes longer than any fixed
    /// budget, and cancelling a nearly-complete download would be worse than waiting. Stalls are caught by
    /// the per-read timeout in <see cref="DownloadAsync"/> instead, which is the failure that actually
    /// happens (HttpClient.Timeout would not cover the body stream here anyway — with
    /// ResponseHeadersRead it stops applying once the headers arrive).</summary>
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>How long a single read may stall before the download is abandoned.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    private readonly IAppPaths _paths;

    public WhisperModelStore(IAppPaths paths) => _paths = paths;

    public static string FileName(TranscriptModelSize size) => $"ggml-{size.ToString().ToLowerInvariant()}.bin";

    public string PathFor(TranscriptModelSize size) => Path.Combine(ModelDirectory, FileName(size));

    private string ModelDirectory => Path.Combine(_paths.DataDirectory, "models");

    public bool IsAvailable(TranscriptModelSize size)
    {
        string path = PathFor(size);
        return File.Exists(path) && new FileInfo(path).Length > MinimumPlausibleBytes(size);
    }

    /// <summary>Real download sizes (bytes) — shown to the user before anything is fetched, because "this will
    /// download 142 MB" is exactly the kind of thing that must not happen silently in a privacy-first app.</summary>
    public long ExpectedBytes(TranscriptModelSize size) => size switch
    {
        TranscriptModelSize.Tiny => 77_691_713,
        TranscriptModelSize.Base => 147_951_465,
        _ => 487_601_967,
    };

    public string DisplayName(TranscriptModelSize size) => size switch
    {
        TranscriptModelSize.Tiny => Resources.Strings.Transcripts_ModelTiny,
        TranscriptModelSize.Base => Resources.Strings.Transcripts_ModelBase,
        _ => Resources.Strings.Transcripts_ModelSmall,
    };

    /// <summary>SHA-256 of each model file at <see cref="PinnedRevision"/>, taken from the repository's own
    /// LFS metadata. The sizes in <see cref="ExpectedBytes"/> come from the same source and match, which is a
    /// useful cross-check that these refer to the same artifacts.</summary>
    private static string ExpectedSha256(TranscriptModelSize size) => size switch
    {
        TranscriptModelSize.Tiny => "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21",
        TranscriptModelSize.Base => "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe",
        _ => "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b",
    };

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    /// <summary>A partial download or a truncated file must not be accepted as a model — the lower bound is
    /// ~90% of the real size, since the exact byte count isn't guaranteed stable across mirror revisions.</summary>
    private static long MinimumPlausibleBytes(TranscriptModelSize size) => size switch
    {
        TranscriptModelSize.Tiny => 60_000_000,
        TranscriptModelSize.Base => 120_000_000,
        _ => 400_000_000,
    };

    public async Task DownloadAsync(TranscriptModelSize size, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelDirectory);
        string destination = PathFor(size);
        string partial = destination + ".partial";

        // Pinned to an exact repository revision, not the moving `main` branch, and verified against a
        // recorded SHA-256 below — the same standard this project holds the bundled ffmpeg to. A GGML file is
        // parsed by native whisper.cpp code rather than being inert data, so "it downloaded over HTTPS" is
        // not sufficient on its own.
        string url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{PinnedRevision}/{FileName(size)}";
        long expected = ExpectedBytes(size);

        Log.Information("Downloading Whisper model {Size} ({Mb} MB) — one-time, opt-in", size, expected / (1024 * 1024));

        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using (Stream source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                // Each read gets its own deadline: a black-holed connection mid-body otherwise parks this
                // task forever (the response headers already arrived, so nothing else is watching it).
                while (true)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readTimeout.CancelAfter(ReadTimeout);
                    try
                    {
                        read = await source.ReadAsync(buffer, readTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new IOException($"The download stalled for more than {ReadTimeout.TotalSeconds:0} seconds.");
                    }

                    if (read <= 0)
                    {
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    progress?.Report(expected > 0 ? Math.Min(1.0, (double)total / expected) : 0);
                }
            }

            if (new FileInfo(partial).Length < MinimumPlausibleBytes(size))
            {
                throw new IOException("The downloaded model is smaller than expected — the download was truncated.");
            }

            // Verified BEFORE the rename, so a file that fails the check never appears under the real name:
            // IsAvailable only looks at the final path, and a half-trusted model must never become reachable.
            string actual = await Sha256Async(partial, ct);
            string expectedHash = ExpectedSha256(size);
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error("Whisper model hash mismatch for {Size}: expected {Expected}, got {Actual}", size, expectedHash, actual);
                throw new IOException("The downloaded model didn't match its expected checksum, so it wasn't used.");
            }

            File.Move(partial, destination, overwrite: true);
            Log.Information("Whisper model ready: {Path}", destination);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static void TryDelete(string path)
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
            // A leftover .partial is harmless: IsAvailable only ever looks at the final name.
        }
    }
}
