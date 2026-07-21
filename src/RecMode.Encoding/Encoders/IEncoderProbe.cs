namespace RecMode.Encoding.Encoders;

/// <summary>
/// Detects which catalog encoders the bundled ffmpeg actually exposes (plan §3.2 — <c>ffmpeg -encoders</c>).
/// A full trial-encode probe per candidate arrives in Phase 3; Phase 1 uses the encoder list, which is
/// enough to populate the combo with only-present entries.
/// </summary>
public interface IEncoderProbe
{
    /// <summary>Runs the probe once and caches the result for the process lifetime.</summary>
    IReadOnlyList<EncoderInfo> GetAvailableEncoders();
}
