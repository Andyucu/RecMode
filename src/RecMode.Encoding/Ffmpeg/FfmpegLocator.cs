using System.Security.Cryptography;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using Serilog;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>
/// Default <see cref="IFfmpegLocator"/>. Resolution order (plan §3.4):
/// <list type="number">
///   <item>User override path in settings (validated to exist).</item>
///   <item>Bundled build under <c>AppPaths.FfmpegDirectory</c>, hash-verified against the manifest if present.</item>
/// </list>
/// Never throws — a missing/invalid ffmpeg becomes an unavailable result carrying a <see cref="RecModeError"/>.
/// <para>
/// Caches its result for as long as the resolved override setting doesn't change: resolving hashes both
/// (100+ MB) bundled binaries against the pinned manifest, and this is called repeatedly within a single run
/// (once to populate the Record screen's encoder list, again on every recording start) — nothing about the
/// files on disk changes between those calls, so re-hashing every time was pure repeated I/O for no benefit.
/// Recomputes automatically if <see cref="RecModeSettings.FfmpegPathOverride"/> changes, so changing it in
/// Settings still takes effect without an app restart.
/// </para>
/// </summary>
public sealed class FfmpegLocator(IAppPaths paths, ISettingsService settings) : IFfmpegLocator
{
    private const string FfmpegExe = "ffmpeg.exe";
    private const string FfprobeExe = "ffprobe.exe";

    private readonly Lock _cacheLock = new();
    private FfmpegResolution? _cached;
    private string? _cachedOverridePath;

    public FfmpegResolution Resolve()
    {
        string? overridePath = settings.Current.FfmpegPathOverride;
        lock (_cacheLock)
        {
            if (_cached is not null && string.Equals(_cachedOverridePath, overridePath, StringComparison.Ordinal))
            {
                return _cached;
            }
        }

        FfmpegResolution result;
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            result = ResolveBundled();
        }
        else
        {
            result = ResolveFromOverride(overridePath);

            // A stale override must not block recording when a perfectly good bundled build is sitting next
            // to the exe. This is the normal portable case, not an edge case: point the override at
            // D:\tools\ffmpeg on one machine, move the folder to a machine with no D: drive, and recording
            // used to refuse to start with "fix the path in Settings" — while .\ffmpeg\ffmpeg.exe, hash-pinned
            // and ready, went unused. The override is an optimization, so fall back and warn rather than fail.
            if (!result.IsAvailable)
            {
                FfmpegResolution bundled = ResolveBundled();
                if (bundled.IsAvailable)
                {
                    Log.Warning("The configured ffmpeg override {Path} is unusable; falling back to the bundled build at {Bundled}",
                        overridePath, bundled.FfmpegPath);
                    result = bundled with
                    {
                        Error = RecModeError.Warning(
                            "ffmpeg.override-fallback",
                            "The ffmpeg path in Settings couldn't be used, so the bundled build is being used instead.",
                            "Clear or fix the custom ffmpeg path in Settings to stop seeing this."),
                    };
                }
            }
        }

        // Only cache a successful resolution. An "unavailable" result exists so the user can fix the
        // problem (e.g. drop ffmpeg into .\ffmpeg\, as the error message itself suggests) — caching it would
        // keep reporting that same stale error on every later call until the app restarts, even after the
        // user does exactly what the message asked. A cache hit's entire purpose is skipping the expensive
        // SHA-256 hashing of two 100+ MB binaries, which only happens on the success path in the first place.
        if (result.IsAvailable)
        {
            lock (_cacheLock)
            {
                _cached = result;
                _cachedOverridePath = overridePath;
            }
        }

        return result;
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
        if (manifest is null)
        {
            return new FfmpegResolution
            {
                IsAvailable = true,
                FfmpegPath = ffmpegPath,
                FfprobePath = File.Exists(ffprobePath) ? ffprobePath : null,
                Source = FfmpegSource.Bundled,
                HashVerified = false,
                Error = RecModeError.Warning(
                    "ffmpeg.manifest-absent",
                    "The ffmpeg build wasn't hash-verified (no manifest present)."),
            };
        }

        (bool verified, RecModeError? error) = VerifyHashes(manifest, ffmpegPath, ffprobePath);
        if (error?.Severity == ErrorSeverity.BlockingError)
        {
            // A pinned hash that doesn't match means the bundled binary was modified or corrupted since it
            // was built — unlike "no manifest at all" or "manifest present but pins nothing" above/below
            // (nothing to check, so best-effort continues), this is an active integrity failure. Continuing
            // to run the mismatched binary anyway (the previous behavior: a Warning, but still IsAvailable)
            // defeated the entire point of pinning it — the one threat this check is shaped for (the binary
            // was tampered with or corrupted after being built) is exactly the case it used to let through.
            return Unavailable(FfmpegSource.Bundled, error);
        }

        return new FfmpegResolution
        {
            IsAvailable = true,
            FfmpegPath = ffmpegPath,
            FfprobePath = File.Exists(ffprobePath) ? ffprobePath : null,
            Source = FfmpegSource.Bundled,
            HashVerified = verified,
            Error = error,
        };
    }

    /// <summary>Blocking on an actual mismatch (a pinned hash that doesn't match); Warning if the manifest is
    /// present but pins nothing to check (same non-blocking treatment as no manifest at all — see
    /// <see cref="ResolveBundled"/>); Verified=true only when at least one hash was actually pinned and
    /// matched — a manifest with both fields blank previously reported <c>HashVerified = true</c> despite
    /// nothing having been checked at all.</summary>
    private static (bool Verified, RecModeError? Error) VerifyHashes(
        FfmpegManifest manifest, string ffmpegPath, string ffprobePath)
    {
        bool ffmpegPinned = !string.IsNullOrWhiteSpace(manifest.FfmpegSha256);
        bool ffprobePinned = File.Exists(ffprobePath) && !string.IsNullOrWhiteSpace(manifest.FfprobeSha256);

        try
        {
            if (ffmpegPinned && !Matches(manifest.FfmpegSha256, ffmpegPath))
            {
                return (false, RecModeError.Blocking(
                    "ffmpeg.hash-mismatch",
                    "The bundled ffmpeg.exe doesn't match its pinned hash.",
                    "The build may have been modified or corrupted. Reinstall RecMode or set a custom ffmpeg path in Settings."));
            }

            if (ffprobePinned && !Matches(manifest.FfprobeSha256, ffprobePath))
            {
                return (false, RecModeError.Blocking(
                    "ffprobe.hash-mismatch",
                    "The bundled ffprobe.exe doesn't match its pinned hash.",
                    "The build may have been modified or corrupted. Reinstall RecMode or set a custom ffmpeg path in Settings."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File.Exists(ffmpegPath) passing doesn't mean File.OpenRead will succeed a moment later — an AV
            // scanner or backup agent can hold the file open without FILE_SHARE_READ right as this class's
            // own doc comment promises "never throws". Best-effort, same as "no manifest"/"manifest empty"
            // below: the binary is still usable as a subprocess even though our own read handle failed, so
            // this degrades to "not hash-verified" instead of taking down whichever caller asked for the
            // encoder list (historically the Record screen's first paint, on the UI thread).
            return (false, RecModeError.Warning(
                "ffmpeg.hash-check-failed",
                "The bundled ffmpeg build couldn't be hash-verified right now.",
                "Another program may have the file open. Recording can still proceed.", ex));
        }

        if (!ffmpegPinned && !ffprobePinned)
        {
            return (false, RecModeError.Warning(
                "ffmpeg.manifest-empty",
                "The ffmpeg build wasn't hash-verified (the manifest has no pinned hashes)."));
        }

        return (true, null);
    }

    private static bool Matches(string expectedHex, string filePath) =>
        string.Equals(ComputeSha256(filePath), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);

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
