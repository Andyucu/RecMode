namespace RecMode.Core.Infrastructure;

/// <summary>
/// Single point where every OS-version-dependent capability is decided (plan OS matrix). Checked once
/// at startup; no other code sniffs the Windows version. Win11 is primary; the floor is Win10 2004
/// (build 19041), set by process-loopback audio and <c>WDA_EXCLUDEFROMCAPTURE</c>.
/// </summary>
public interface IOsCapabilities
{
    /// <summary>Full OS build number (e.g. 22631).</summary>
    int BuildNumber { get; }

    /// <summary>True on Windows 11 (build ≥ 22000).</summary>
    bool IsWindows11 { get; }

    /// <summary>True when the running OS meets RecMode's floor (Win10 2004 / 19041).</summary>
    bool MeetsMinimumOs { get; }

    /// <summary>WGC yellow-border suppression (<c>IsBorderRequired=false</c>) — Win11 only.</summary>
    bool SupportsCaptureBorderSuppression { get; }

    /// <summary>DWM Mica / acrylic backdrops — Win11 only; Win10 falls back to opaque themed fills.</summary>
    bool SupportsMicaBackdrop { get; }

    /// <summary>Immersive dark title bar (DWM attribute) — Win10 19041+ and Win11.</summary>
    bool SupportsImmersiveDarkTitleBar { get; }

    /// <summary>Per-application (process-loopback) audio capture — Win10 2004+ / Win11.</summary>
    bool SupportsProcessLoopbackAudio { get; }

    /// <summary>Excluding the toolbar/overlay windows from capture (<c>WDA_EXCLUDEFROMCAPTURE</c>) — Win10 2004+.</summary>
    bool SupportsExcludeFromCapture { get; }
}
