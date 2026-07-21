using System.Runtime.Versioning;

namespace RecMode.Core.Infrastructure;

/// <summary>
/// Default <see cref="IOsCapabilities"/>. Derives everything from the OS build number, resolved once at
/// construction. On modern .NET with a Windows-10-aware app.manifest, <see cref="Environment.OSVersion"/>
/// reports the true build, so no P/Invoke to <c>RtlGetVersion</c> is needed here.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OsCapabilities : IOsCapabilities
{
    // Build floors.
    private const int Win10_2004 = 19041; // process loopback audio, WDA_EXCLUDEFROMCAPTURE, dark title bar
    private const int Win11 = 22000;       // Mica, WGC border suppression

    public OsCapabilities(int? overrideBuildNumber = null)
    {
        BuildNumber = overrideBuildNumber ?? Environment.OSVersion.Version.Build;
    }

    public int BuildNumber { get; }

    public bool IsWindows11 => BuildNumber >= Win11;
    public bool MeetsMinimumOs => BuildNumber >= Win10_2004;
    public bool SupportsCaptureBorderSuppression => BuildNumber >= Win11;
    public bool SupportsMicaBackdrop => BuildNumber >= Win11;
    public bool SupportsImmersiveDarkTitleBar => BuildNumber >= Win10_2004;
    public bool SupportsProcessLoopbackAudio => BuildNumber >= Win10_2004;
    public bool SupportsExcludeFromCapture => BuildNumber >= Win10_2004;
}
