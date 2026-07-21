namespace RecMode.Encoding.Ffmpeg;

/// <summary>
/// Resolves the ffmpeg/ffprobe executables at startup: a user-provided override path wins, otherwise
/// the bundled build under <c>AppPaths.FfmpegDirectory</c>; verifies pinned SHA-256 hashes when a
/// manifest is present (plan §3.4).
/// </summary>
public interface IFfmpegLocator
{
    FfmpegResolution Resolve();
}
