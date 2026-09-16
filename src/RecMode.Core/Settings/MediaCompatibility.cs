namespace RecMode.Core.Settings;

/// <summary>
/// Which video codecs each container can hold (plan §3.3 codec matrix). ISOBMFF containers (MP4/MOV) and
/// Matroska (MKV) take H.264/HEVC/AV1; WebM is AV1-only here (VP9 is deliberately excluded). Pure → testable.
/// </summary>
public static class MediaCompatibility
{
    /// <summary>Interleaved channel count of the audio pipe when separate tracks are muxed: three stereo
    /// pairs, in the order mixed / mic / system. Shared by the mixer that writes the pipe (<c>RecMode.Audio</c>)
    /// and the ffmpeg argument builder that splits it (<c>RecMode.Encoding</c>) so the two can't drift — they
    /// live in different assemblies with no reference between them. See
    /// <see cref="RecModeSettings.SeparateAudioTracks"/>.</summary>
    public const int SeparateAudioChannelCount = 6;

    public static bool IsVideoCompatible(VideoCodec codec, MediaContainer container) => container switch
    {
        MediaContainer.WebM => codec == VideoCodec.Av1,
        _ => true, // MP4 / MOV / MKV accept H.264, HEVC and AV1
    };

    /// <summary>A short reason a codec/container pair is invalid, for the pre-flight message (empty when valid).</summary>
    public static string IncompatibilityReason(VideoCodec codec, MediaContainer container) =>
        IsVideoCompatible(codec, container)
            ? ""
            : $"{codec} can't be stored in {container}. Choose MKV, or an AV1 encoder for WebM.";

    /// <summary>Whether a container can carry multiple audio tracks (the mixed track plus distinct mic/system
    /// tracks — see <c>RecModeSettings.SeparateAudioTracks</c>). Matroska and QuickTime handle it cleanly;
    /// MP4's multi-audio support is poor and WebM is Opus-only in practice.</summary>
    public static bool SupportsSeparateAudioTracks(MediaContainer container) =>
        container is MediaContainer.Mkv or MediaContainer.Mov;
}
