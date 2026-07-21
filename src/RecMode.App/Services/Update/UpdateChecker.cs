using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace RecMode.App.Services;

public enum UpdateCheckStatus { NotConfigured, UpToDate, UpdateAvailable, Failed }

public sealed record UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }
    public string? Version { get; init; }
    public string? ReleasesPageUrl { get; init; }
    public bool CanApply { get; init; }
    public string? Error { get; init; }
}

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
    Task ApplyAndRestartAsync(CancellationToken ct = default);
}

/// <summary>Uses GitHub Releases for installed auto-updates and portable ZIP download notifications.</summary>
public sealed class UpdateChecker : IUpdateChecker
{
    public const string GitHubRepositoryUrl = "https://github.com/Andyucu/RecMode";
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/Andyucu/RecMode/releases?per_page=20";
    private const string PortableAssetName = "RecMode-win-Portable.zip";
    private static readonly HttpClient Http = CreateHttpClient();
    private UpdateManager? _mgr;
    private UpdateInfo? _pending;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(GitHubRepositoryUrl, accessToken: null, prerelease: true));
            UpdateInfo? info = await mgr.CheckForUpdatesAsync();
            if (info is null) return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };

            _mgr = mgr;
            _pending = info;
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                Version = info.TargetFullRelease.Version.ToString(),
                CanApply = true,
            };
        }
        catch (NotInstalledException)
        {
            // A portable/dev copy must not replace its own possibly removable/read-only folder.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.Message };
        }

        return await CheckPortableAsync(ct);
    }

    private static async Task<UpdateCheckResult> CheckPortableAsync(CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(GitHubReleasesApiUrl, ct);
            response.EnsureSuccessStatusCode();
            List<GitHubRelease>? releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(cancellationToken: ct);
            GitHubRelease? release = releases?.FirstOrDefault(r => !r.Draft && r.Assets.Any(a =>
                string.Equals(a.Name, PortableAssetName, StringComparison.OrdinalIgnoreCase)));
            GitHubReleaseAsset? asset = release?.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, PortableAssetName, StringComparison.OrdinalIgnoreCase));

            if (release is null) return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            if (release.TagName is null || asset?.BrowserDownloadUrl is null || !TryParseVersion(release.TagName, out Version latest))
                return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = "The portable release metadata was invalid." };

            return latest > CurrentVersion
                ? new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Version = release.TagName, ReleasesPageUrl = asset.BrowserDownloadUrl }
                : new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.Message };
        }
    }

    public async Task ApplyAndRestartAsync(CancellationToken ct = default)
    {
        if (_mgr is null || _pending is null) return;
        await _mgr.DownloadUpdatesAsync(_pending, cancelToken: ct);
        _mgr.ApplyUpdatesAndRestart(_pending);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RecMode-Updater/1.0");
        return client;
    }

    private static Version CurrentVersion => TryParseVersion(
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0", out Version version)
        ? version : new Version(0, 0, 0);

    private static bool TryParseVersion(string value, out Version version) =>
        Version.TryParse(value.Trim().TrimStart('v', 'V').Split('+', '-')[0], out version!);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("assets")] public List<GitHubReleaseAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
