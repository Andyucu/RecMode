using System.Security.Cryptography;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>
/// Default <see cref="IFfmpegLocator"/>. Resolution order (plan §3.4):
/// <list type="number">
///   <item>User override path in settings (validated to exist).</item>
///   <item>Bundled build under <c>AppPaths.FfmpegDirectory</c>, hash-verified against the manifest if present.</item>
/// </list>
/// Never throws — a missing/invalid ffmpeg becomes an unavailable result carrying a <see cref="RecModeError"/>.
/// </summary>
public sealed class FfmpegLocator(IAppPaths paths, ISettingsService settings) : IFfmpegLocator
{
    private const string FfmpegExe = "ffmpeg.exe";
    private const string FfprobeExe = "ffprobe.exe";

    public FfmpegResolution Resolve()
    {
        string? overridePath = settings.Current.FfmpegPathOverride;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return ResolveFromOverride(overridePath);
        }

        return ResolveBundled();
    }

    private static FfmpegResolution ResolveFromOverride(string overridePath)
    {
        // The override can point at ffmpeg.exe directly or at the folder containing it.
        string ffmpegPath = Directory.Exists(overridePath)
            ? Path.Combine(overridePath, FfmpegExe)
            : overridePath;
        string dir = Path.GetDirectoryName(ffmpegPath) ?? overridePath;
        string ffprobePath = Path.Combine(dir, FfprobeExe);

        if (!File.Exists(ffmpegPath))
        {
            return Unavailable(FfmpegSource.UserOverride, RecModeError.Blocking(
                "ffmpeg.override-missing",
                "The ffmpeg path in Settings doesn't point to ffmpeg.exe.",
                "Fix the path in Settings or clear it to use the bundled build."));
        }

        return new FfmpegResolution
        {
            IsAvailable = true,
            FfmpegPath = ffmpegPath,
            FfprobePath = File.Exists(ffprobePath) ? ffprobePath : null,
            Source = FfmpegSource.UserOverride,
            HashVerified = false, // user builds aren't pinned
        };
    }

    private FfmpegResolution ResolveBundled()
    {
        string dir = paths.FfmpegDirectory;
        string ffmpegPath = Path.Combine(dir, FfmpegExe);
        string ffprobePath = Path.Combine(dir, FfprobeExe);

        if (!File.Exists(ffmpegPath))
        {
            return Unavailable(FfmpegSource.Bundled, RecModeError.Blocking(
                "ffmpeg.bundled-missing",
                "The bundled ffmpeg wasn't found.",
                "Reinstall RecMode or set a custom ffmpeg path in Settings."));
        }

        FfmpegManifest? manifest = FfmpegManifest.TryLoad(dir);
        bool verified = false;
        RecModeError? warning = null;

        if (manifest is null)
        {
            warning = RecModeError.Warning(
                "ffmpeg.manifest-absent",
                "The ffmpeg build wasn't hash-verified (no manifest present).");
        }
        else
        {
            (verified, warning) = VerifyHashes(manifest, ffmpegPath, ffprobePath);
        }

        return new FfmpegResolution
        {
            IsAvailable = true,
            FfmpegPath = ffmpegPath,
            FfprobePath = File.Exists(ffprobePath) ? ffprobePath : null,
            Source = FfmpegSource.Bundled,
            HashVerified = verified,
            Error = warning,
        };
    }

    private static (bool Verified, RecModeError? Warning) VerifyHashes(
        FfmpegManifest manifest, string ffmpegPath, string ffprobePath)
    {
        if (!MatchesIfPinned(manifest.FfmpegSha256, ffmpegPath))
        {
            return (false, RecModeError.Warning(
                "ffmpeg.hash-mismatch",
                "The bundled ffmpeg.exe doesn't match its pinned hash.",
                "The build may have been modified; recording still works but verify the source."));
        }

        if (File.Exists(ffprobePath) && !MatchesIfPinned(manifest.FfprobeSha256, ffprobePath))
        {
            return (false, RecModeError.Warning(
                "ffprobe.hash-mismatch",
                "The bundled ffprobe.exe doesn't match its pinned hash."));
        }

        return (true, null);
    }

    private static bool MatchesIfPinned(string expectedHex, string filePath)
    {
        if (string.IsNullOrWhiteSpace(expectedHex))
        {
            return true; // nothing pinned → nothing to fail
        }

        return string.Equals(ComputeSha256(filePath), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static FfmpegResolution Unavailable(FfmpegSource source, RecModeError error) => new()
    {
        IsAvailable = false,
        Source = source,
        Error = error,
    };
}
