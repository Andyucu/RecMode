namespace RecMode.Core.Settings;

/// <summary>
/// Which video codecs each container can hold (plan §3.3 codec matrix). ISOBMFF containers (MP4/MOV) and
/// Matroska (MKV) take H.264/HEVC/AV1; WebM is AV1-only here (VP9 is deliberately excluded). Pure → testable.
/// </summary>
public static class MediaCompatibility
{
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
}
