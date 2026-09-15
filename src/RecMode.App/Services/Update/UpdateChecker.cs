using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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
            // Bound how long the CALLER waits for the Velopack check. Velopack performs its own GitHub HTTP
            // requests with no timeout of its own (its CheckForUpdatesAsync takes no CancellationToken in
            // this package version), and CheckForUpdatesOnLaunch defaults true — so a black-holed network
            // (captive portal, a corporate proxy that drops instead of rejecting) used to park the
            // launch-time fire-and-forget task indefinitely. WaitAsync bounds the await only — the
            // underlying request keeps running on a threadpool thread regardless — so its eventual outcome
            // is observed explicitly below: a late failure must not surface as an unobserved-task exception
            // long after this method returned. The 10s ceiling mirrors CheckPortableAsync's HttpClient
            // timeout; TimeoutException is caught further down and reported as a normal check failure,
            // which the launch-time path treats as silent.
            Task<UpdateInfo?> velopackCheck = mgr.CheckForUpdatesAsync();
            _ = velopackCheck.ContinueWith(
                static t => _ = t.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            UpdateInfo? info = await velopackCheck.WaitAsync(TimeSpan.FromSeconds(10), ct);
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or TimeoutException)
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

            GitHubRelease? release = SelectNewestPortableRelease(releases);
            GitHubReleaseAsset? asset = release?.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, PortableAssetName, StringComparison.OrdinalIgnoreCase));

            if (release?.TagName is null || asset?.BrowserDownloadUrl is null ||
                !TryParseVersion(release.TagName, out Version latest))
            {
                // No usable portable release found. Deliberately "up to date" rather than "failed": an empty
                // or unparseable release list is not an error the user can act on, and reporting a failure
                // for it would be indistinguishable from a real connectivity problem.
                return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
            }

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

    /// <summary>Parses an untrusted GitHub release tag (<c>v0.9.63-beta</c>, <c>0.9.63</c>, <c>V1.0.0+sha</c>,
    /// or garbage) into a comparable <see cref="Version"/> — gates whether the update prompt appears at all,
    /// so a parse failure here must fail closed (return false) rather than throw. Internal, not private, so
    /// it's directly unit-testable against the actual range of tag shapes GitHub releases can produce.</summary>
    internal static bool TryParseVersion(string value, out Version version) =>
        Version.TryParse(value.Trim().TrimStart('v', 'V').Split('+', '-')[0], out version!);

    /// <summary>
    /// Picks the release with the highest <em>version</em> that actually ships the portable asset. The GitHub
    /// releases API returns newest-<em>created</em> first, and this used to just take element [0] — so
    /// publishing a hotfix for an older line (0.9.70.1 after 0.9.80 already shipped) put that hotfix at the
    /// front, `0.9.70.1 &gt; 0.9.75` compared false, and every portable user was told "you're up to date"
    /// indefinitely, because no other release was ever examined. Skips drafts, releases without the portable
    /// asset, and unparseable tags rather than letting any one of them decide the outcome.
    /// Internal, not private, so the selection rule is unit-testable without a live GitHub response.
    /// </summary>
    internal static GitHubRelease? SelectNewestPortableRelease(List<GitHubRelease>? releases)
    {
        GitHubRelease? best = null;
        Version? bestVersion = null;

        foreach (GitHubRelease release in releases ?? [])
        {
            if (release.Draft || release.TagName is null ||
                !release.Assets.Any(a => string.Equals(a.Name, PortableAssetName, StringComparison.OrdinalIgnoreCase)) ||
                !TryParseVersion(release.TagName, out Version version))
            {
                continue;
            }

            if (bestVersion is null || version > bestVersion)
            {
                best = release;
                bestVersion = version;
            }
        }

        return best;
    }

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("assets")] public List<GitHubReleaseAsset> Assets { get; set; } = [];
    }

    internal sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
