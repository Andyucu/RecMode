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

    // Encoding defaults
    public VideoCodec Codec { get; set; } = VideoCodec.H264;
    public EncoderBackend Backend { get; set; } = EncoderBackend.Auto;
    public MediaContainer Container { get; set; } = MediaContainer.Mp4;
    public bool HardwareEncoding { get; set; } = true;
    public int FrameRate { get; set; } = 60;

    /// <summary>0–100 quality slider; mapped to CRF/CQ in the encoding layer (CRF = 51 − q·0.38).</summary>
    public int Quality { get; set; } = 70;

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

    // Performance (bounds computed from hardware probe in Phase 3/9; 0 = auto)
    public int CpuThreadCap { get; set; }
    public EncoderEffort Effort { get; set; } = EncoderEffort.Balanced;
    public bool BelowNormalEncoderPriority { get; set; } = true;

    // Preview
    public int PreviewFps { get; set; } = 30;

    // Hotkeys (default F9/F10/F11 per plan)
    public string HotkeyStartStop { get; set; } = "F9";
    public string HotkeyPauseResume { get; set; } = "F10";
    public string HotkeyScreenshot { get; set; } = "F11";

    // FFmpeg — null = use the bundled build under AppPaths.FfmpegDirectory (§3.4).
    public string? FfmpegPathOverride { get; set; }

    // System integration & privacy
    public bool StartWithWindows { get; set; }
    public bool CheckForUpdatesOnLaunch { get; set; } = true;

    /// <summary>Opt-in local crash minidumps (§3.6). Off by default — privacy is a feature.</summary>
    public bool EnableCrashMinidumps { get; set; }

    // Scheduled recordings (Phase 6 UI + data model; the firing engine is Phase 8).
    public List<ScheduleItem> Schedules { get; set; } = [];

    /// <summary>Deep copy for handing out immutable snapshots and detecting changes.</summary>
    public RecModeSettings Clone() => (RecModeSettings)MemberwiseClone();
}
