using System.Globalization;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>Everything needed to build one recording's ffmpeg command line.</summary>
public sealed record FfmpegJob
{
    public required EncoderInfo Encoder { get; init; }
    public required MediaContainer Container { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int FrameRate { get; init; }

    /// <summary>0–100 quality slider.</summary>
    public required int Quality { get; init; }

    public required string PipeName { get; init; }
    public required string OutputPath { get; init; }

    /// <summary>Set to add a second (audio) input: f32le 48 kHz stereo over this named pipe.</summary>
    public string? AudioPipeName { get; init; }
    public AudioCodec AudioCodec { get; init; } = AudioCodec.Aac;
    public int AudioBitrateKbps { get; init; } = 192;

    /// <summary>Software-encoder thread cap (§3.3). 0 = ffmpeg default (all cores). Ignored by hardware encoders.</summary>
    public int CpuThreadCap { get; init; }

    /// <summary>Run the ffmpeg process below normal priority so recording doesn't starve foreground work (§3.3).</summary>
    public bool BelowNormalPriority { get; init; }

    /// <summary>Encoder effort tier (§3.3) → per-encoder preset. Balanced = the default preset for each encoder.</summary>
    public EncoderEffort Effort { get; init; } = EncoderEffort.Balanced;
}

/// <summary>
/// Builds the ffmpeg argument list for a recording (plan §3.3). Video-only for Phase 1 (audio arrives in
/// Phase 4). Quality maps to per-encoder rate control off the design's model: CRF = 51 − q·0.38, with the
/// hardware encoders using an equivalent CQ/QP. Kept deterministic so Phase 3 can snapshot-test it.
/// </summary>
public static class FfmpegArgsBuilder
{
    public static string Build(FfmpegJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Validate(job);

        string videoIn =
            $"-f rawvideo -pix_fmt nv12 -s {job.Width}x{job.Height} -r {job.FrameRate} " +
            $"-i \\\\.\\pipe\\{job.PipeName}";

        string audioIn = "", audioMap = "", audioEnc = "";
        if (job.AudioPipeName is not null)
        {
            audioIn = $"-f f32le -ar 48000 -ac 2 -i \\\\.\\pipe\\{job.AudioPipeName}";
            audioMap = "-map 0:v:0 -map 1:a:0";
            audioEnc = BuildAudioArgs(job.Container, job.AudioCodec, job.AudioBitrateKbps);
        }

        string encoder = BuildEncoderArgs(job.Encoder, job.Quality, job.Effort);
        string faststart = job.Container == MediaContainer.Mp4 ? "-movflags +faststart" : "";

        // Thread cap only bites on software encoders (hardware offloads to the GPU/ASIC), so don't emit it for hw.
        string threads = job.CpuThreadCap > 0 && !job.Encoder.IsHardware
            ? $"-threads {job.CpuThreadCap} "
            : "";

        return $"-hide_banner -loglevel warning {videoIn} {audioIn} {audioMap} " +
               $"{threads}{encoder} -pix_fmt yuv420p {audioEnc} {faststart} -y \"{job.OutputPath}\"";
    }

    /// <summary>Rejects an <see cref="FfmpegJob"/> that would produce a malformed or nonsensical command line,
    /// so a bad job fails fast at the boundary instead of surfacing as a cryptic ffmpeg process failure.</summary>
    private static void Validate(FfmpegJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Encoder.FfmpegId))
        {
            throw new ArgumentException("Encoder.FfmpegId must be set.", nameof(job));
        }
        if (job.Width <= 0 || job.Width % 2 != 0)
        {
            throw new ArgumentException($"Width must be a positive even number, got {job.Width}.", nameof(job));
        }
        if (job.Height <= 0 || job.Height % 2 != 0)
        {
            throw new ArgumentException($"Height must be a positive even number, got {job.Height}.", nameof(job));
        }
        if (job.FrameRate <= 0)
        {
            throw new ArgumentException($"FrameRate must be positive, got {job.FrameRate}.", nameof(job));
        }
        if (job.CpuThreadCap < 0)
        {
            throw new ArgumentException($"CpuThreadCap can't be negative, got {job.CpuThreadCap}.", nameof(job));
        }
        if (job.AudioPipeName is not null && job.AudioBitrateKbps <= 0)
        {
            throw new ArgumentException($"AudioBitrateKbps must be positive, got {job.AudioBitrateKbps}.", nameof(job));
        }
        if (string.IsNullOrWhiteSpace(job.OutputPath))
        {
            throw new ArgumentException("OutputPath must be set.", nameof(job));
        }
        ValidatePipeName(job.PipeName, nameof(job.PipeName));
        if (job.AudioPipeName is not null)
        {
            ValidatePipeName(job.AudioPipeName, nameof(job.AudioPipeName));
        }
    }

    /// <summary>Pipe names are interpolated directly into a <c>\\.\pipe\{name}</c> path, so they must not
    /// contain path separators or be empty — either would produce a malformed or unintended pipe path.</summary>
    private static void ValidatePipeName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains('/'))
        {
            throw new ArgumentException($"'{name}' is not a valid pipe name.", paramName);
        }
    }

    /// <summary>Audio codec steered by container (plan §3.3): MP4/MOV→AAC, MKV/WebM→Opus, with FLAC where valid.</summary>
    public static string BuildAudioArgs(MediaContainer container, AudioCodec requested, int bitrateKbps)
    {
        AudioCodec codec = container switch
        {
            MediaContainer.WebM => AudioCodec.Opus,                                   // WebM = Opus only
            MediaContainer.Mp4 or MediaContainer.Mov => requested == AudioCodec.Flac  // MP4/MOV can't do Opus
                ? AudioCodec.Aac : requested == AudioCodec.Opus ? AudioCodec.Aac : requested,
            _ => requested, // MKV takes anything
        };

        return codec switch
        {
            AudioCodec.Opus => $"-c:a libopus -b:a {bitrateKbps}k",
            AudioCodec.Flac => "-c:a flac",
            _ => $"-c:a aac -b:a {bitrateKbps}k",
        };
    }

    /// <summary>CRF from the design model, clamped to a sane encoder range.</summary>
    public static int QualityToCrf(int quality)
    {
        double crf = 51 - quality * 0.38;
        return Math.Clamp((int)Math.Round(crf), 1, 51);
    }

    private static string BuildEncoderArgs(EncoderInfo encoder, int quality, EncoderEffort effort)
    {
        int crf = QualityToCrf(quality);
        string c = crf.ToString(CultureInfo.InvariantCulture);

        return encoder.FfmpegId switch
        {
            "libx264" => $"-c:v libx264 -preset {X264Preset(effort)} -crf {c}",
            "libx265" => $"-c:v libx265 -preset {X264Preset(effort)} -crf {c}",
            "libsvtav1" => $"-c:v libsvtav1 -preset {SvtAv1Preset(effort)} -crf {c}",

            "h264_nvenc" or "hevc_nvenc" or "av1_nvenc" =>
                $"-c:v {encoder.FfmpegId} -preset {NvencPreset(effort)} -rc vbr -cq {c}",

            // AMF: cqp is the simplest honest mapping for Phase 1 (qvbr tuning comes in Phase 3).
            "h264_amf" or "hevc_amf" or "av1_amf" =>
                $"-c:v {encoder.FfmpegId} -usage transcoding -quality {AmfQuality(effort)} -rc cqp -qp_i {c} -qp_p {c}",

            "h264_qsv" or "hevc_qsv" or "av1_qsv" =>
                $"-c:v {encoder.FfmpegId} -preset {QsvPreset(effort)} -global_quality {c}",

            _ => $"-c:v {encoder.FfmpegId} -crf {c}",
        };
    }

    // Per-encoder effort → preset. Balanced deliberately keeps each encoder's established default.
    private static string X264Preset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "ultrafast",
        EncoderEffort.Quality => "medium",
        _ => "veryfast",
    };

    private static string SvtAv1Preset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "10",
        EncoderEffort.Quality => "6",
        _ => "8",
    };

    private static string NvencPreset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "p2",
        EncoderEffort.Quality => "p6",
        _ => "p4",
    };

    private static string AmfQuality(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "speed",
        EncoderEffort.Quality => "quality",
        _ => "balanced",
    };

    // QSV presets are veryfast..veryslow (no "ultrafast").
    private static string QsvPreset(EncoderEffort e) => e switch
    {
        EncoderEffort.Fast => "veryfast",
        EncoderEffort.Quality => "slow",
        _ => "medium",
    };
}
