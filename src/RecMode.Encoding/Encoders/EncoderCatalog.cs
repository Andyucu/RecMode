using RecMode.Core.Settings;

namespace RecMode.Encoding.Encoders;

/// <summary>
/// The full set of encoders RecMode knows how to drive (plan §3.3 matrix). The probe intersects this with
/// what the bundled ffmpeg actually reports as available. Validation rigor is H.264-first until MVP ships.
/// </summary>
public static class EncoderCatalog
{
    public static readonly IReadOnlyList<EncoderInfo> All =
    [
        // H.264
        new() { FfmpegId = "h264_nvenc", DisplayName = "H.264 · NVIDIA NVENC", Codec = VideoCodec.H264, Backend = EncoderBackend.Nvenc },
        new() { FfmpegId = "h264_amf",   DisplayName = "H.264 · AMD AMF",       Codec = VideoCodec.H264, Backend = EncoderBackend.Amf },
        new() { FfmpegId = "h264_qsv",   DisplayName = "H.264 · Intel QSV",     Codec = VideoCodec.H264, Backend = EncoderBackend.Qsv },
        new() { FfmpegId = "libx264",    DisplayName = "H.264 · Software (x264)", Codec = VideoCodec.H264, Backend = EncoderBackend.Software },

        // HEVC
        new() { FfmpegId = "hevc_nvenc", DisplayName = "HEVC · NVIDIA NVENC", Codec = VideoCodec.Hevc, Backend = EncoderBackend.Nvenc },
        new() { FfmpegId = "hevc_amf",   DisplayName = "HEVC · AMD AMF",       Codec = VideoCodec.Hevc, Backend = EncoderBackend.Amf },
        new() { FfmpegId = "hevc_qsv",   DisplayName = "HEVC · Intel QSV",     Codec = VideoCodec.Hevc, Backend = EncoderBackend.Qsv },
        new() { FfmpegId = "libx265",    DisplayName = "HEVC · Software (x265)", Codec = VideoCodec.Hevc, Backend = EncoderBackend.Software },

        // AV1
        new() { FfmpegId = "av1_nvenc",  DisplayName = "AV1 · NVIDIA NVENC", Codec = VideoCodec.Av1, Backend = EncoderBackend.Nvenc },
        new() { FfmpegId = "av1_amf",    DisplayName = "AV1 · AMD AMF",       Codec = VideoCodec.Av1, Backend = EncoderBackend.Amf },
        new() { FfmpegId = "av1_qsv",    DisplayName = "AV1 · Intel QSV",     Codec = VideoCodec.Av1, Backend = EncoderBackend.Qsv },
        new() { FfmpegId = "libsvtav1",  DisplayName = "AV1 · Software (SVT-AV1)", Codec = VideoCodec.Av1, Backend = EncoderBackend.Software },
    ];
}
