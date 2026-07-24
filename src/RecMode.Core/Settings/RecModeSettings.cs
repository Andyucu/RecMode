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
    public MediaContainer Container { get; set; } = MediaContainer.Mp4;
    public bool HardwareEncoding { get; set; } = true;
    public int FrameRate { get; set; } = 60;

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
    /// Per-app audio (plan §7): when set, "System audio" captures only this process's audio instead of the
    /// whole system. Persisted by process *name* (PIDs aren't stable across restarts) and re-resolved to a
    /// live PID at recording start; null/empty means full-system loopback (the default, unchanged behavior).
    /// </summary>
    public string? PerAppAudioProcessName { get; set; }

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

    // Preview
    public int PreviewFps { get; set; } = 30;

    // Hotkeys (default F8/F9/F10/F11 per plan + profile-cycling feature)
    public string HotkeyNextProfile { get; set; } = "F8";
    public string HotkeyStartStop { get; set; } = "F9";
    public string HotkeyPauseResume { get; set; } = "F10";
    public string HotkeyScreenshot { get; set; } = "F11";

    /// <summary>
    /// Window source helper: when true, RecMode re-resolves the selected window by process/title before
    /// preview/record/screenshot so apps that recreate their HWND can still be captured without repicking.
    /// WGC already follows movement/resizing while the original HWND remains alive.
    /// </summary>
    public bool FollowWindow { get; set; } = true;

    // FFmpeg — null = use the bundled build under AppPaths.FfmpegDirectory (§3.4).
    public string? FfmpegPathOverride { get; set; }

    // System integration & privacy
    public bool StartWithWindows { get; set; }
    public bool CheckForUpdatesOnLaunch { get; set; } = true;

    /// <summary>Opt-in local crash minidumps (§3.6). Off by default — privacy is a feature.</summary>
    public bool EnableCrashMinidumps { get; set; }

    // Scheduled recordings (Phase 6 UI + data model; the firing engine is Phase 8).
    public List<ScheduleItem> Schedules { get; set; } = [];

    // Recording profiles (plan §7 backlog #4, pulled forward): user-created presets alongside the built-in
    // ones. Null/unknown SelectedProfileName means "Custom" — the Record screen's settings are edited directly.
    public List<RecordingProfile> CustomProfiles { get; set; } = [];
    public string? SelectedProfileName { get; set; }

    /// <summary>Deep copy for handing out immutable snapshots and detecting changes.</summary>
    public RecModeSettings Clone() => (RecModeSettings)MemberwiseClone();
}
