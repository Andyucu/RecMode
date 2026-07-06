using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Velopack;
using Velopack.Exceptions;

namespace RecMode.App.Services;

public enum UpdateCheckStatus
{
    /// <summary>No feed configured yet (default) — checks quietly do nothing. No accidental phone-home.</summary>
    NotConfigured,
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }
    public string? Version { get; init; }

    /// <summary>Human-facing link (portable mode: nothing to apply in place, so this is the notify+link).</summary>
    public string? ReleasesPageUrl { get; init; }

    /// <summary>True only when running as a genuine Velopack-managed install and an update was found — <see cref="IUpdateChecker.ApplyAndRestartAsync"/> is meaningful.</summary>
    public bool CanApply { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Checks for a newer RecMode release and, if running as a real Velopack-managed install, can download and
/// apply it. Best-effort and silent by default (plan §3.5 / "no telemetry, ever" spirit — this contacts a
/// URL, but only one carrying no user data, and only when a feed is actually configured).
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);

    /// <summary>Downloads and installs the update found by the last <see cref="CheckAsync"/> call, then restarts. No-op if that check didn't report <c>CanApply</c>.</summary>
    Task ApplyAndRestartAsync(CancellationToken ct = default);
}

/// <summary>
/// Two feeds, both blank until real hosting/signing is decided (plan §3.5 — Velopack installer is a later
/// add-on sharing this code): <see cref="VelopackFeedUrl"/> is the real Velopack package feed, used when this
/// process is a genuine Velopack-managed install (can download + apply in place); <see cref="PortableManifestUrl"/>
/// is a tiny, hand-rolled <c>{ "version", "releasesUrl" }</c> JSON used in portable mode, where there's no
/// installed copy to update in place — it only ever notifies with a link, per the plan ("just notifies +
/// links"). <see cref="Velopack.Exceptions.NotInstalledException"/> is how a genuine Velopack install is told
/// apart from a portable/dev run — Velopack doesn't expose a plain "am I installed" property.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    // Set these once real hosting/signing exists; both blank = update checks always report NotConfigured.
    public const string VelopackFeedUrl = "";
    public const string PortableManifestUrl = "";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private UpdateManager? _mgr;
    private UpdateInfo? _pending;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(VelopackFeedUrl))
        {
            try
            {
                var mgr = new UpdateManager(VelopackFeedUrl);
                UpdateInfo? info = await mgr.CheckForUpdatesAsync();
                if (info is null)
                {
                    return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
                }

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
                // Not a genuine Velopack install (dev/portable run) — fall through to the portable check.
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException)
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.Message };
            }
        }

        return await CheckPortableAsync(ct);
    }

    private static async Task<UpdateCheckResult> CheckPortableAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PortableManifestUrl))
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.NotConfigured };
        }

        try
        {
            using HttpResponseMessage resp = await Http.GetAsync(PortableManifestUrl, ct);
            resp.EnsureSuccessStatusCode();
            PortableManifest? manifest = await resp.Content.ReadFromJsonAsync<PortableManifest>(cancellationToken: ct);

            Version? current = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            if (manifest?.Version is null || current is null || !Version.TryParse(manifest.Version, out Version? latest))
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = "The update manifest was malformed." };
            }

            return latest > current
                ? new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpdateAvailable,
                    Version = manifest.Version,
                    ReleasesPageUrl = manifest.ReleasesUrl,
                }
                : new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new UpdateCheckResult { Status = UpdateCheckStatus.Failed, Error = ex.Message };
        }
    }

    public async Task ApplyAndRestartAsync(CancellationToken ct = default)
    {
        if (_mgr is null || _pending is null)
        {
            return;
        }

        await _mgr.DownloadUpdatesAsync(_pending, cancelToken: ct);
        _mgr.ApplyUpdatesAndRestart(_pending);
    }

    private sealed class PortableManifest
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("releasesUrl")]
        public string? ReleasesUrl { get; set; }
    }
}
