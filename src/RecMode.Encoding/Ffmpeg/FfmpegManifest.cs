using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>
/// Pinned build identity for the bundled ffmpeg (plan §3.4 — SHA-256 recorded at build time, verified
/// at startup). Shipped as <c>ffmpeg.manifest.json</c> next to the binaries. Hashes are lowercase hex.
/// </summary>
public sealed class FfmpegManifest
{
    public const string FileName = "ffmpeg.manifest.json";

    /// <summary>Human label of the pinned build, e.g. "gyan.dev 7.1 full (GPL)".</summary>
    [JsonPropertyName("build")]
    public string Build { get; set; } = "";

    /// <summary>Expected SHA-256 of ffmpeg.exe (lowercase hex), or empty to skip.</summary>
    [JsonPropertyName("ffmpegSha256")]
    public string FfmpegSha256 { get; set; } = "";

    /// <summary>Expected SHA-256 of ffprobe.exe (lowercase hex), or empty to skip.</summary>
    [JsonPropertyName("ffprobeSha256")]
    public string FfprobeSha256 { get; set; } = "";

    public static FfmpegManifest? TryLoad(string ffmpegDirectory)
    {
        string path = Path.Combine(ffmpegDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FfmpegManifest>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }
}
