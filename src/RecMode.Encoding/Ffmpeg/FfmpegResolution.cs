using RecMode.Core.Errors;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>Result of resolving ffmpeg/ffprobe at startup (plan §3.4).</summary>
public sealed record FfmpegResolution
{
    /// <summary>True when both ffmpeg and ffprobe were found (and, if a manifest exists, hash-verified).</summary>
    public required bool IsAvailable { get; init; }

    public string? FfmpegPath { get; init; }
    public string? FfprobePath { get; init; }

    /// <summary>Which build was used.</summary>
    public FfmpegSource Source { get; init; }

    /// <summary>True when a pinned-hash manifest was present and every hash matched.</summary>
    public bool HashVerified { get; init; }

    /// <summary>Populated when <see cref="IsAvailable"/> is false, or a warning (e.g. hash mismatch/skip).</summary>
    public RecModeError? Error { get; init; }
}

public enum FfmpegSource
{
    None,
    Bundled,
    UserOverride,
}
