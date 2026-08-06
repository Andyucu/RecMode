using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecMode.Core.Settings;

/// <summary>
/// The persisted settings document (plan §3.2 — JSON, versioned schema with migration). Plain mutable
/// POCO so it round-trips cleanly and binds later; ViewModels wrap it. Null folder paths mean "use the
/// <c>AppPaths</c> default", keeping portability intact.
/// </summary>
public sealed class RecModeSettings
{
    /// <summary>Bump this when the shape changes; add a step to <c>SettingsMigrator</c> for the upgrade.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// Properties present in <c>settings.json</c> that this build doesn't know about — captured on load and
    /// written back out verbatim on save. <see cref="SettingsMigrator"/> deliberately leaves a *higher*
    /// <see cref="SchemaVersion"/> alone ("genuinely newer docs keep their higher number"), but that intent
    /// wasn't actually implemented: deserializing into this class dropped every property it doesn't declare,
    /// and the next save re-serialized the lossy object over the file. Running an older portable copy once
    /// against a shared <c>Data\</c> folder therefore erased all of the newer build's settings — schedules
    /// with new fields, profiles, any new toggle — permanently, on the first slider drag.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }

    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.System;
    public AccentColor Accent { get; set; } = AccentColor.Blue;
    public ShellLayout Layout { get; set; } = ShellLayout.Sidebar;

    // Output — null = use AppPaths default (portable-safe).
    public string? OutputFolder { get; set; }
    public string? ScreenshotFolder { get; set; }
    public string FilenamePattern { get; set; } = "RecMode {date} {time}";

    // Last region selection (monitor-local pixels; Width 0 = unset). Persisted per plan Phase 2.
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionWidth { get; set; }
    public int RegionHeight { get; set; }

    // Recording behavior
    public int CountdownSeconds { get; set; } = 3;
    public bool CaptureCursor { get; set; } = true;
    public bool HighlightClicks { get; set; }

    /// <summary>Keystroke visualizer: shows hotkey combos (e.g. "Ctrl + Z") on screen while recording.</summary>
    public bool ShowKeystrokes { get; set; }

    /// <summary>Smart auto-zoom (beta): GPU pan/zoom toward each mouse click while recording, easing back out
    /// after a short idle period. Monitor/Region sources only — see <c>RecordingCoordinator.ComputeZoomRect</c>.</summary>
    public bool AutoZoomEnabled { get; set; }

    // Encoding defaults
    public VideoCodec Codec { get; set; } = VideoCodec.H264;
    public EncoderBackend Backend { get; set; } = EncoderBackend.Auto;
    public MediaContainer Container { get; set; } = MediaContainer.Mkv;
    public int FrameRate { get; set; } = 30;

    /// <summary>0–100 quality slider; mapped to CRF/CQ/QP in the encoding layer via a perceptually-curved,
    /// per-encoder-calibrated model (see <c>FfmpegArgsBuilder.EffectiveQualityValue</c>).</summary>
    public int Quality { get; set; } = 70;

    /// <summary>Captured-video brightness adjustment, -100 (darkest) .. 100 (brightest), 0 = unchanged.
    /// Applied on the GPU VideoProcessor pass; no-ops on hardware without a Brightness filter.</summary>
    public double Brightness { get; set; }

    /// <summary>Adds a generous <c>-maxrate/-bufsize</c> ceiling (derived from resolution/fps/quality) alongside
    /// CRF/CQ encoding, so unusually complex content (fast motion, busy screen content) can't produce a
    /// surprise multi-GB file — the ceiling is set well above the typical bitrate for the chosen quality, so it
    /// rarely engages. Applies only where the encoder's rate-control mode supports it without changing behavior
    /// (software x264/x265/SVT-AV1, and NVENC's existing VBR mode); AMF's constant-QP mode and QSV's ICQ mode
    /// don't support a bitrate ceiling without switching rate-control modes entirely, so they're left alone.</summary>
    public bool BitrateGuardrailEnabled { get; set; } = true;

    /// <summary>Safe-recording: capture to MKV then auto-remux to MP4 on stop (plan §3, on by default).</summary>
    public bool SafeRecording { get; set; } = true;

    /// <summary>Auto-split (plan §3.3 Phase 3 tail): roll over to a new file once a segment hits <see cref="AutoSplitSizeMb"/>.</summary>
    public bool AutoSplitEnabled { get; set; }

    /// <summary>Segment size threshold in MB. Default ~3.9 GB — safely under the FAT32 4 GB single-file limit.</summary>
    public int AutoSplitSizeMb { get; set; } = 3900;

    // Audio defaults
    public bool SystemAudioEnabled { get; set; } = true;
    public bool MicrophoneEnabled { get; set; }
    public AudioCodec AudioCodec { get; set; } = AudioCodec.Aac;
    public int AudioBitrateKbps { get; set; } = 192;

    /// <summary>Per-source capture volume, 0–100 (→ mixer gain). 100 = unity.</summary>
    public int SystemVolume { get; set; } = 100;
    public int MicVolume { get; set; } = 100;

    /// <summary>
    /// A/V sync offset in milliseconds, applied to the recorded audio track. Positive delays audio (use when
    /// audio runs ahead of the picture), negative advances it. 0 = no adjustment.
    /// <para>
    /// Defaults to 0 deliberately. RecMode's own measurements found a consistent direction (video lagging
    /// audio) but with per-marker jitter of the same order as the offset itself, and only on this dev box's
    /// GDI fallback capture path — not the WGC path most users are on. Shipping a non-zero default derived
    /// from that would be guessing, and a wrong constant makes sync worse rather than better. This is
    /// therefore a manual escape hatch, matching what every comparable recorder ships (OBS's per-source
    /// "Sync Offset" is likewise manual and defaults to 0); see PROJECT_MEMORY.md's 2026-07-29 entry for the
    /// measurements and the reasoning.
    /// </para>
    /// </summary>
    public int AudioSyncOffsetMs { get; set; }

    /// <summary>
    /// Per-app audio (plan §7): when set, "System audio" captures only this process's audio instead of the
    /// whole system. Persisted by process *name* (PIDs aren't stable across restarts) and re-resolved to a
    /// live PID at recording start; null/empty means full-system loopback (the default, unchanged behavior).
    /// </summary>
    public string? PerAppAudioProcessName { get; set; }

    /// <summary>
    /// Which physical playback devices "System audio" loops back, by WASAPI endpoint ID. Null (the default)
    /// means auto-detect: the default Console-role device, plus the Communications-role device too if it's a
    /// different physical output (see <c>AudioMixer.StartCommsLoopbackIfDifferent</c> — covers VoIP apps like
    /// Teams that route call audio there specifically). A non-null list is an explicit user selection from the
    /// Settings audio-device picker — capture exactly those devices, however many, instead of auto-detecting.
    /// Ignored when <see cref="PerAppAudioProcessName"/> is set (per-app targeting already names an exact
    /// process, not a device). Device IDs are stable across reboots but not guaranteed across driver
    /// reinstalls; a since-vanished ID is simply skipped at capture time, same as any other unplugged device.
    /// </summary>
    public List<string>? SystemAudioDeviceIds { get; set; }

    /// <summary>
    /// Whether auto-detect (<see cref="SystemAudioDeviceIds"/> null) also loops back the Communications-role
    /// playback device when it differs from the Console-role default — see
    /// <c>AudioMixer.StartCommsLoopbackIfDifferent</c>'s doc comment for why that matters for VoIP apps like
    /// Teams/Zoom. <b>Default true, by explicit user request (2026-08-06)</b> — catching Teams/Zoom call
    /// audio by default was judged more valuable than the echo risk below. Be aware: capturing two devices at
    /// once sums two independently-clocked WASAPI captures, and on setups where the two roles carry
    /// correlated or overlapping audio (a common shape — Windows often assigns a paired Bluetooth headset to
    /// the Communications role while Console stays the speakers) the resulting phase mismatch between the two
    /// captures is audible as echo/doubling, even for users who never asked for multi-device capture at all.
    /// The Settings toggle (<c>SettingsViewModel.CaptureCommunicationsRoleAudio</c>) is the escape hatch for
    /// anyone who hits that.
    /// </summary>
    public bool CaptureCommunicationsRoleAudio { get; set; } = true;

    /// <summary>
    /// Webcam picture-in-picture overlay (Phase 7). Null <see cref="WebcamDeviceId"/> means "not configured" —
    /// enabling the toggle with no device selected does nothing (fails closed, no accidental default-camera use).
    /// </summary>
    public bool WebcamEnabled { get; set; }
    public string? WebcamDeviceId { get; set; }
    public WebcamOverlayPosition WebcamPosition { get; set; } = WebcamOverlayPosition.BottomRight;

    /// <summary>PIP box width as a percentage of the output frame width, 10–50.</summary>
    public int WebcamSizePercent { get; set; } = 20;

    // Performance (bounds computed from hardware probe in Phase 3/9; 0 = auto)
    public int CpuThreadCap { get; set; }
    public EncoderEffort Effort { get; set; } = EncoderEffort.Balanced;
    public bool BelowNormalEncoderPriority { get; set; } = true;

    // Hotkeys (default F8/F9/F10/F11 per plan + profile-cycling feature)
    public string HotkeyNextProfile { get; set; } = "F8";
    public string HotkeyStartStop { get; set; } = "F9";
    public string HotkeyPauseResume { get; set; } = "F10";
    public string HotkeyScreenshot { get; set; } = "F11";

    /// <summary>Default is a modifier chord, not a bare letter — a global hotkey with no modifier would fire
    /// on every press of that key system-wide, including normal typing in any other app.</summary>
    public string HotkeyMicMute { get; set; } = "Ctrl+Shift+M";

    /// <summary>
    /// Window source helper: when true, RecMode re-resolves the selected window by process/title before
    /// preview/record/screenshot so apps that recreate their HWND can still be captured without repicking.
    /// WGC already follows movement/resizing while the original HWND remains alive.
    /// </summary>
    public bool FollowWindow { get; set; } = true;

    /// <summary>
    /// Last dragged position of the floating recording toolbar, in device-independent virtual-screen
    /// coordinates (so it can live on any monitor, not just the primary), only meaningful while
    /// <see cref="ToolbarPinned"/> is true. Unpinned recordings always open at the default
    /// bottom-centre-of-primary placement — direct user request: the bar should be predictable by default,
    /// with pinning as the explicit opt-in for "remember where I put it."
    /// </summary>
    public double? ToolbarLeft { get; set; }
    public double? ToolbarTop { get; set; }

    /// <summary>When true, the floating recording toolbar reopens at <see cref="ToolbarLeft"/>/<see cref="ToolbarTop"/>
    /// on every new recording instead of resetting to the default bottom-centre-of-primary position.</summary>
    public bool ToolbarPinned { get; set; }

    // FFmpeg — null = use the bundled build under AppPaths.FfmpegDirectory (§3.4).
    public string? FfmpegPathOverride { get; set; }

    // System integration & privacy
    public bool StartWithWindows { get; set; }

    /// <summary>When true, the caption-bar close (×) button hides the window to the tray instead of quitting —
    /// mirroring minimize, which already always hides to tray (<c>TrayIconService</c>). Off by default so
    /// existing users' expectation that × exits is unchanged; the tray menu's own "Quit" always exits either way.</summary>
    public bool CloseToTray { get; set; }

    public bool CheckForUpdatesOnLaunch { get; set; } = true;

    /// <summary>Opt-in local crash minidumps (§3.6). Off by default — privacy is a feature.</summary>
    public bool EnableCrashMinidumps { get; set; }

    // Scheduled recordings (Phase 6 UI + data model; the firing engine is Phase 8).
    //
    // Both list properties coalesce null in their setters rather than relying on the initializer alone.
    // System.Text.Json assigns null for an explicit `"Schedules": null` in the file, which the initializer
    // does NOT protect against — and neither consumer null-checks: SchedulerService.Tick foreach-es Schedules
    // on a ~20s DispatcherTimer (so a null throws an NRE, and pops the "unexpected error" modal, every 20s
    // for the rest of the session) and RecordViewModel.Profiles' LoadProfiles feeds CustomProfiles straight
    // into RecordingProfiles.Merge on the Record screen's first paint. Neither is caught by Load()'s
    // corrupt-file recovery, because such a file parses perfectly well — the same class of "valid JSON,
    // invalid value" gap LenientEnumConverterFactory closes for enums. Reachable via a hand-edited file, a
    // partially-restored backup, or another local account in the shared-ACL portable case FolderAclCheck warns about.
    private List<ScheduleItem> _schedules = [];
    public List<ScheduleItem> Schedules { get => _schedules; set => _schedules = value ?? []; }

    // Recording profiles (plan §7 backlog #4, pulled forward): user-created presets alongside the built-in
    // ones. Null/unknown SelectedProfileName means "Custom" — the Record screen's settings are edited directly.
    private List<RecordingProfile> _customProfiles = [];
    public List<RecordingProfile> CustomProfiles { get => _customProfiles; set => _customProfiles = value ?? []; }
    public string? SelectedProfileName { get; set; }
}
