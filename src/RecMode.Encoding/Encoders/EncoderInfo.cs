using RecMode.Core.Settings;

namespace RecMode.Encoding.Encoders;

/// <summary>A concrete ffmpeg encoder the app can offer (one cell of the codec × backend matrix, §3.3).</summary>
public sealed record EncoderInfo
{
    /// <summary>ffmpeg encoder id, e.g. <c>h264_amf</c>, <c>libx264</c>.</summary>
    public required string FfmpegId { get; init; }

    /// <summary>Combo label, e.g. "H.264 · AMD AMF" or "H.264 · Software (x264)".</summary>
    public required string DisplayName { get; init; }

    public required VideoCodec Codec { get; init; }
    public required EncoderBackend Backend { get; init; }

    /// <summary>True for fixed-function GPU encoders (NVENC/AMF/QSV) — the plan's default when present (§3.9).</summary>
    public bool IsHardware => Backend is not EncoderBackend.Software;

    /// <summary>Short badge for the Record screen, e.g. "GPU · AMF" / "CPU · software".</summary>
    public string HardwareBadge => IsHardware ? $"GPU · {Backend}" : "CPU · software";

    public override string ToString() => DisplayName;
}
