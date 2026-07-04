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

        string encoder = BuildEncoderArgs(job.Encoder, job.Quality);
        string faststart = job.Container == MediaContainer.Mp4 ? "-movflags +faststart" : "";

        return $"-hide_banner -loglevel warning {videoIn} {audioIn} {audioMap} " +
               $"{encoder} -pix_fmt yuv420p {audioEnc} {faststart} -y \"{job.OutputPath}\"";
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

    private static string BuildEncoderArgs(EncoderInfo encoder, int quality)
    {
        int crf = QualityToCrf(quality);
        string c = crf.ToString(CultureInfo.InvariantCulture);

        return encoder.FfmpegId switch
        {
            "libx264" => $"-c:v libx264 -preset veryfast -crf {c}",
            "libx265" => $"-c:v libx265 -preset veryfast -crf {c}",
            "libsvtav1" => $"-c:v libsvtav1 -preset 8 -crf {c}",

            "h264_nvenc" or "hevc_nvenc" or "av1_nvenc" =>
                $"-c:v {encoder.FfmpegId} -preset p4 -rc vbr -cq {c}",

            // AMF: cqp is the simplest honest mapping for Phase 1 (qvbr tuning comes in Phase 3).
            "h264_amf" or "hevc_amf" or "av1_amf" =>
                $"-c:v {encoder.FfmpegId} -usage transcoding -quality balanced -rc cqp -qp_i {c} -qp_p {c}",

            "h264_qsv" or "hevc_qsv" or "av1_qsv" =>
                $"-c:v {encoder.FfmpegId} -global_quality {c}",

            _ => $"-c:v {encoder.FfmpegId} -crf {c}",
        };
    }
}
