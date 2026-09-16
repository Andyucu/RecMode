using System.Diagnostics;
using System.IO;
using System.Text;
using RecMode.App.Resources;
using RecMode.Core.Errors;
using RecMode.Core.Settings;
using RecMode.Encoding.Ffmpeg;
using Serilog;
using Whisper.net;

namespace RecMode.App.Services.Transcripts;

/// <summary>
/// Turns a finished recording into a caption/transcript sidecar, entirely on this machine (plan §7
/// "transcripts"). Nothing is uploaded — which is the whole point: every comparable product sends the file
/// away to get captions.
/// <para>
/// Deliberately captions the <b>microphone</b> stream when the recording has one, not the mix: the mixed track
/// also carries system audio (music, calls, notification chimes), which measurably degrades recognition, and
/// the mic-only stream is what the speaker actually said. Stream titles are written at mux time for exactly
/// this reason (see <c>FfmpegArgsBuilder</c>); a single-track or untitled file falls back to the first audio
/// stream, which is the best that can be done for a recording made before the titles existed.
/// </para>
/// </summary>
public interface ITranscriptionService
{
    /// <summary>Transcribes <paramref name="mediaPath"/> and writes <c>.srt</c>/<c>.vtt</c> beside it.
    /// Returns null (after reporting) when it can't run — no ffmpeg, no model, no audio stream.</summary>
    Task<TranscriptDocument?> TranscribeAsync(string mediaPath, TranscriptModelSize size,
        IProgress<string>? status = null, CancellationToken ct = default);

    /// <summary>The sidecar caption files for a recording, in the same folder.</summary>
    static string SrtPathFor(string mediaPath) => Path.ChangeExtension(mediaPath, ".srt");

    static string VttPathFor(string mediaPath) => Path.ChangeExtension(mediaPath, ".vtt");

    /// <summary>Loads a previously written transcript, or null when the recording has none.</summary>
    static TranscriptDocument? LoadSidecar(string mediaPath)
    {
        string path = SrtPathFor(mediaPath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return TranscriptFormats.ParseSrt(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Default <see cref="ITranscriptionService"/> — ffmpeg to 16 kHz mono PCM, then whisper.cpp.</summary>
public sealed class TranscriptionService(IFfmpegLocator ffmpeg, IWhisperModelStore models, IErrorReporter errors)
    : ITranscriptionService
{
    public async Task<TranscriptDocument?> TranscribeAsync(string mediaPath, TranscriptModelSize size,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        if (!File.Exists(mediaPath))
        {
            errors.Warn("transcript.missing-file", Strings.Transcript_MissingFile);
            return null;
        }

        FfmpegResolution resolution = ffmpeg.Resolve();
        if (resolution.FfmpegPath is null)
        {
            errors.Warn("transcript.no-ffmpeg", Strings.Transcript_NoFfmpeg);
            return null;
        }

        if (!models.IsAvailable(size))
        {
            errors.Warn("transcript.no-model", Strings.Transcript_NoModel, Strings.Transcript_NoModelHint);
            return null;
        }

        string wavPath = Path.Combine(Path.GetTempPath(), $"recmode-stt-{Guid.NewGuid():N}.wav");
        try
        {
            status?.Report(Strings.Transcript_StatusExtracting);
            int streamIndex = await FindSpeechStreamAsync(resolution.FfprobePath, mediaPath, ct);
            if (streamIndex == NoAudioStream)
            {
                errors.Warn("transcript.no-audio", Strings.Transcript_NoAudio);
                return null;
            }

            if (!await ExtractAudioAsync(resolution.FfmpegPath, mediaPath, streamIndex, wavPath, ct))
            {
                errors.Warn("transcript.extract-failed", Strings.Transcript_ExtractFailed);
                return null;
            }

            status?.Report(Strings.Transcript_StatusTranscribing);
            var segments = new List<TranscriptSegment>();
            using (WhisperFactory factory = WhisperFactory.FromPath(models.PathFor(size)))
            using (var processor = factory.CreateBuilder().WithLanguage("auto").Build())
            {
                await using (FileStream audio = File.OpenRead(wavPath))
                await foreach (SegmentData segment in processor.ProcessAsync(audio, ct))
                {
                    string text = segment.Text.Trim();
                    if (text.Length > 0)
                    {
                        segments.Add(new TranscriptSegment(segment.Start.TotalSeconds, segment.End.TotalSeconds, text));
                    }
                }
            }

            var document = new TranscriptDocument(segments);
            WriteSidecars(mediaPath, document);
            Log.Information("Transcribed {Path}: {Count} segment(s)", mediaPath, segments.Count);
            return document;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Transcription failed for {Path}", mediaPath);
            errors.Warn("transcript.failed", Strings.Transcript_Failed, Strings.Transcript_FailedHint);
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(wavPath))
                {
                    File.Delete(wavPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp WAV is harmless; the OS temp cleaner will take it.
            }
        }
    }

    private static void WriteSidecars(string mediaPath, TranscriptDocument document)
    {
        // UTF-8 with BOM: unlike the ffmetadata chapter file, caption sidecars are *supposed* to carry one —
        // Windows players and editors may otherwise render non-ASCII text in the wrong code page.
        var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllText(ITranscriptionService.SrtPathFor(mediaPath), TranscriptFormats.ToSrt(document.Segments), utf8WithBom);
        File.WriteAllText(ITranscriptionService.VttPathFor(mediaPath), TranscriptFormats.ToVtt(document.Segments), utf8WithBom);
    }

    /// <summary>The file has no audio stream at all.</summary>
    private const int NoAudioStream = -1;

    /// <summary>Couldn't determine which stream to use (no ffprobe) — fall back to "the first audio stream".</summary>
    private const int UnknownStream = -2;

    /// <summary>Picks the audio stream to transcribe: the one titled "Microphone" when it exists, else the
    /// first audio stream. Returns an <b>absolute</b> stream index (ffprobe's <c>stream=index</c>, i.e. the
    /// index among ALL streams including video), <see cref="NoAudioStream"/> when the file has no audio, or
    /// <see cref="UnknownStream"/> when there's no ffprobe to ask.
    /// <para>
    /// The absolute/relative distinction is the whole reason these are named constants. ffprobe reports
    /// absolute indices — a normal recording's single audio track is index <b>1</b>, because the video is 0 —
    /// while <c>-map 0:a:N</c> counts only audio streams. Feeding one to the other asks for audio stream #1 of
    /// a file that has exactly one (#0): ffmpeg fails outright with "Stream map '' matches no streams", so
    /// transcription failed on every ordinary recording, and on a multi-track file it silently selected the
    /// wrong track. <see cref="ExtractAudioAsync"/> therefore maps <c>0:{absolute}</c>, never <c>0:a:{n}</c>.
    /// </para></summary>
    private static async Task<int> FindSpeechStreamAsync(string? ffprobePath, string mediaPath, CancellationToken ct)
    {
        if (ffprobePath is null || !File.Exists(ffprobePath))
        {
            return UnknownStream;
        }

        string output = await RunCaptureAsync(ffprobePath,
            $"-v error -select_streams a -show_entries stream=index:stream_tags=title -of csv=p=0 \"{mediaPath}\"", ct);

        int first = -1;
        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split(',', 2);
            if (!int.TryParse(parts[0], out int index))
            {
                continue;
            }

            if (first < 0)
            {
                first = index;
            }

            if (parts.Length > 1 && parts[1].Trim().Equals("Microphone", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return first < 0 ? NoAudioStream : first;
    }

    private static async Task<bool> ExtractAudioAsync(string ffmpegPath, string mediaPath, int streamIndex,
        string wavPath, CancellationToken ct)
    {
        // 16 kHz mono PCM is exactly what whisper.cpp wants, so the model never has to resample.
        // ABSOLUTE stream index (0:N), not the audio-relative form (0:a:N) — see FindSpeechStreamAsync for
        // why mixing those up broke transcription on every recording. UnknownStream means "no ffprobe to ask",
        // in which case the audio-relative "first audio stream" is exactly the right fallback.
        string map = streamIndex >= 0 ? $"-map 0:{streamIndex}" : "-map 0:a:0";
        string arguments =
            $"-v error -y -i \"{mediaPath}\" {map} -ac 1 -ar 16000 -c:a pcm_s16le \"{wavPath}\"";
        var psi = new ProcessStartInfo(ffmpegPath, arguments)
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

        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0 && File.Exists(wavPath) && new FileInfo(wavPath).Length > 44;
    }

    private static async Task<string> RunCaptureAsync(string exe, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using Process? process = Process.Start(psi);
        if (process is null)
        {
            return "";
        }

        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }
}
