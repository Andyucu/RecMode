using System.Globalization;
using System.Resources;

// Resource keys mirror the .resx names (underscored), which is idiomatic for localization keys.
#pragma warning disable CA1707

namespace RecMode.App.Resources;

/// <summary>
/// Strongly-typed accessor over <c>Strings.resx</c> (plan §1 localization-ready). Hand-written rather than
/// designer-generated so the type is available during WPF markup compilation. Add a property here for each
/// resx key you reference from XAML/code.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("RecMode.App.Resources.Strings", typeof(Strings).Assembly);

    private static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Nav_Record => Get(nameof(Nav_Record));
    public static string Nav_Library => Get(nameof(Nav_Library));
    public static string Nav_Schedule => Get(nameof(Nav_Schedule));
    public static string Nav_Settings => Get(nameof(Nav_Settings));
    public static string Nav_About => Get(nameof(Nav_About));

    public static string Record_SourceHeader => Get(nameof(Record_SourceHeader));
    public static string Source_Screen => Get(nameof(Source_Screen));
    public static string Source_Window => Get(nameof(Source_Window));
    public static string Source_Region => Get(nameof(Source_Region));
    public static string Source_Webcam => Get(nameof(Source_Webcam));

    public static string Record_Display => Get(nameof(Record_Display));
    public static string Record_Video => Get(nameof(Record_Video));
    public static string Record_Encoder => Get(nameof(Record_Encoder));
    public static string Record_Format => Get(nameof(Record_Format));
    public static string Record_FrameRate => Get(nameof(Record_FrameRate));
    public static string Record_Quality => Get(nameof(Record_Quality));
    public static string Record_Brightness => Get(nameof(Record_Brightness));
    public static string Record_QualityPresetWeb => Get(nameof(Record_QualityPresetWeb));
    public static string Record_QualityPresetBalanced => Get(nameof(Record_QualityPresetBalanced));
    public static string Record_QualityPresetArchive => Get(nameof(Record_QualityPresetArchive));
    public static string Record_Stop => Get(nameof(Record_Stop));
    public static string Record_Screenshot => Get(nameof(Record_Screenshot));
    public static string Record_Annotate => Get(nameof(Record_Annotate));

    public static string Source_ScreenTooltip => Get(nameof(Source_ScreenTooltip));
    public static string Source_WindowTooltip => Get(nameof(Source_WindowTooltip));
    public static string Source_RegionTooltip => Get(nameof(Source_RegionTooltip));
    public static string Source_WebcamTooltip => Get(nameof(Source_WebcamTooltip));
    public static string Record_WhichWindowTooltip => Get(nameof(Record_WhichWindowTooltip));
    public static string Record_ManualPick => Get(nameof(Record_ManualPick));
    public static string Record_PickWindowTooltip => Get(nameof(Record_PickWindowTooltip));
    public static string Record_FollowWindow => Get(nameof(Record_FollowWindow));
    public static string Record_FollowWindowTooltip => Get(nameof(Record_FollowWindowTooltip));
    public static string Record_WhichCameraTooltip => Get(nameof(Record_WhichCameraTooltip));
    public static string Record_PreviewPaused => Get(nameof(Record_PreviewPaused));
    public static string Record_StartStopTooltip => Get(nameof(Record_StartStopTooltip));
    public static string Record_PauseResumeTooltip => Get(nameof(Record_PauseResumeTooltip));
    public static string Record_ScreenshotTooltip => Get(nameof(Record_ScreenshotTooltip));
    public static string Record_DiskSpaceTooltip => Get(nameof(Record_DiskSpaceTooltip));
    public static string Record_ProfileTooltip => Get(nameof(Record_ProfileTooltip));
    public static string Record_SaveProfileTooltip => Get(nameof(Record_SaveProfileTooltip));
    public static string Record_DeleteProfileTooltip => Get(nameof(Record_DeleteProfileTooltip));
    public static string Record_DisplayTooltip => Get(nameof(Record_DisplayTooltip));
    public static string Record_Change => Get(nameof(Record_Change));
    public static string Record_ChangeRegionTooltip => Get(nameof(Record_ChangeRegionTooltip));
    public static string Record_EncoderTooltip => Get(nameof(Record_EncoderTooltip));
    public static string Record_FormatTooltip => Get(nameof(Record_FormatTooltip));
    public static string Record_FrameRateTooltip => Get(nameof(Record_FrameRateTooltip));
    public static string Record_QualityTooltip => Get(nameof(Record_QualityTooltip));
    public static string Record_QualityWebTooltip => Get(nameof(Record_QualityWebTooltip));
    public static string Record_QualityBalancedTooltip => Get(nameof(Record_QualityBalancedTooltip));
    public static string Record_QualityArchiveTooltip => Get(nameof(Record_QualityArchiveTooltip));
    public static string Record_BrightnessTooltip => Get(nameof(Record_BrightnessTooltip));
    public static string Record_AudioHeader => Get(nameof(Record_AudioHeader));
    public static string Record_SystemAudioTooltip => Get(nameof(Record_SystemAudioTooltip));
    public static string Record_SystemAudio => Get(nameof(Record_SystemAudio));
    public static string Record_LimitToApp => Get(nameof(Record_LimitToApp));
    public static string Record_SystemVolumeTooltip => Get(nameof(Record_SystemVolumeTooltip));
    public static string Record_Microphone => Get(nameof(Record_Microphone));
    public static string Record_MicrophoneTooltip => Get(nameof(Record_MicrophoneTooltip));
    public static string Record_MicVolumeTooltip => Get(nameof(Record_MicVolumeTooltip));
    public static string Record_WebcamHeader => Get(nameof(Record_WebcamHeader));
    public static string Record_WebcamOverlayTooltip => Get(nameof(Record_WebcamOverlayTooltip));
    public static string Record_ShowWebcam => Get(nameof(Record_ShowWebcam));
    public static string Record_NoCameraDetected => Get(nameof(Record_NoCameraDetected));
    public static string Record_CameraLabel => Get(nameof(Record_CameraLabel));
    public static string Record_WhichCameraOverlayTooltip => Get(nameof(Record_WhichCameraOverlayTooltip));
    public static string Record_PositionLabel => Get(nameof(Record_PositionLabel));
    public static string Record_OverlayPositionTooltip => Get(nameof(Record_OverlayPositionTooltip));
    public static string Record_SizeLabel => Get(nameof(Record_SizeLabel));
    public static string Record_OverlaySizeTooltip => Get(nameof(Record_OverlaySizeTooltip));

    public static string Settings_Appearance => Get(nameof(Settings_Appearance));
    public static string Settings_Theme => Get(nameof(Settings_Theme));
    public static string Settings_Accent => Get(nameof(Settings_Accent));
    public static string Settings_Output => Get(nameof(Settings_Output));
    public static string Settings_Browse => Get(nameof(Settings_Browse));
    public static string Settings_EncodingDefaults => Get(nameof(Settings_EncodingDefaults));
    public static string Settings_Recording => Get(nameof(Settings_Recording));
    public static string Settings_Hotkeys => Get(nameof(Settings_Hotkeys));
    public static string Settings_Performance => Get(nameof(Settings_Performance));
    public static string Settings_Effort => Get(nameof(Settings_Effort));
    public static string Settings_EffortDesc => Get(nameof(Settings_EffortDesc));
    public static string Settings_ThreadCap => Get(nameof(Settings_ThreadCap));
    public static string Settings_ThreadCapDesc => Get(nameof(Settings_ThreadCapDesc));
    public static string Settings_EncoderPriority => Get(nameof(Settings_EncoderPriority));
    public static string Settings_EncoderPriorityDesc => Get(nameof(Settings_EncoderPriorityDesc));
    public static string Settings_General => Get(nameof(Settings_General));
    public static string Settings_ThemeDesc => Get(nameof(Settings_ThemeDesc));
    public static string Settings_AccentDesc => Get(nameof(Settings_AccentDesc));
    public static string Settings_Layout => Get(nameof(Settings_Layout));
    public static string Settings_LayoutDesc => Get(nameof(Settings_LayoutDesc));
    public static string Settings_Encoder => Get(nameof(Settings_Encoder));
    public static string Settings_EncoderDesc => Get(nameof(Settings_EncoderDesc));
    public static string Settings_Container => Get(nameof(Settings_Container));
    public static string Settings_ContainerDesc => Get(nameof(Settings_ContainerDesc));
    public static string Settings_AudioFormat => Get(nameof(Settings_AudioFormat));
    public static string Settings_AudioFormatDesc => Get(nameof(Settings_AudioFormatDesc));
    public static string Settings_AudioBitrate => Get(nameof(Settings_AudioBitrate));
    public static string Settings_AudioBitrateDesc => Get(nameof(Settings_AudioBitrateDesc));
    public static string Settings_AudioSyncOffset => Get(nameof(Settings_AudioSyncOffset));
    public static string Settings_AudioSyncOffsetTooltip => Get(nameof(Settings_AudioSyncOffsetTooltip));
    public static string Settings_AudioSyncOffsetInSync => Get(nameof(Settings_AudioSyncOffsetInSync));
    public static string Settings_AudioSyncOffsetDelayed => Get(nameof(Settings_AudioSyncOffsetDelayed));
    public static string Settings_AudioSyncOffsetAdvanced => Get(nameof(Settings_AudioSyncOffsetAdvanced));
    public static string Settings_SaveTo => Get(nameof(Settings_SaveTo));
    public static string Settings_Pattern => Get(nameof(Settings_Pattern));
    public static string Settings_PatternDesc => Get(nameof(Settings_PatternDesc));
    public static string Settings_Countdown => Get(nameof(Settings_Countdown));
    public static string Settings_CountdownDesc => Get(nameof(Settings_CountdownDesc));
    public static string Settings_Cursor => Get(nameof(Settings_Cursor));
    public static string Settings_CursorDesc => Get(nameof(Settings_CursorDesc));
    public static string Settings_Clicks => Get(nameof(Settings_Clicks));
    public static string Settings_ClicksDesc => Get(nameof(Settings_ClicksDesc));
    public static string Settings_Keystrokes => Get(nameof(Settings_Keystrokes));
    public static string Settings_KeystrokesDesc => Get(nameof(Settings_KeystrokesDesc));
    public static string Settings_AutoZoom => Get(nameof(Settings_AutoZoom));
    public static string Settings_AutoZoomDesc => Get(nameof(Settings_AutoZoomDesc));
    public static string Settings_BitrateGuardrail => Get(nameof(Settings_BitrateGuardrail));
    public static string Settings_BitrateGuardrailDesc => Get(nameof(Settings_BitrateGuardrailDesc));
    public static string Settings_AutoSplit => Get(nameof(Settings_AutoSplit));
    public static string Settings_AutoSplitDesc => Get(nameof(Settings_AutoSplitDesc));
    public static string Settings_KeyboardShortcuts => Get(nameof(Settings_KeyboardShortcuts));
    public static string Settings_HotkeysDesc => Get(nameof(Settings_HotkeysDesc));
    public static string Hotkey_Change => Get(nameof(Hotkey_Change));
    public static string Settings_HkStartStop => Get(nameof(Settings_HkStartStop));
    public static string Settings_HkPauseResume => Get(nameof(Settings_HkPauseResume));
    public static string Settings_HkScreenshot => Get(nameof(Settings_HkScreenshot));
    public static string Settings_Startup => Get(nameof(Settings_Startup));
    public static string Settings_StartupDesc => Get(nameof(Settings_StartupDesc));
    public static string Settings_CloseToTray => Get(nameof(Settings_CloseToTray));
    public static string Settings_CloseToTrayDesc => Get(nameof(Settings_CloseToTrayDesc));
    public static string Settings_CrashMinidumps => Get(nameof(Settings_CrashMinidumps));
    public static string Settings_CrashMinidumpsDesc => Get(nameof(Settings_CrashMinidumpsDesc));
    public static string Settings_Updates => Get(nameof(Settings_Updates));
    public static string Settings_License => Get(nameof(Settings_License));

    public static string Settings_ThemeSystem => Get(nameof(Settings_ThemeSystem));
    public static string Settings_ThemeLight => Get(nameof(Settings_ThemeLight));
    public static string Settings_ThemeDark => Get(nameof(Settings_ThemeDark));
    public static string Settings_AccentBlue => Get(nameof(Settings_AccentBlue));
    public static string Settings_AccentRed => Get(nameof(Settings_AccentRed));
    public static string Settings_AccentPurple => Get(nameof(Settings_AccentPurple));
    public static string Settings_AccentTeal => Get(nameof(Settings_AccentTeal));
    public static string Settings_AccentOrange => Get(nameof(Settings_AccentOrange));
    public static string Settings_LayoutSidebar => Get(nameof(Settings_LayoutSidebar));
    public static string Settings_LayoutTopBar => Get(nameof(Settings_LayoutTopBar));
    public static string Settings_LayoutCompact => Get(nameof(Settings_LayoutCompact));
    public static string Settings_LayoutCompactTooltip => Get(nameof(Settings_LayoutCompactTooltip));
    public static string Settings_OutputFolderTooltip => Get(nameof(Settings_OutputFolderTooltip));
    public static string Settings_HotkeyStartStopTooltip => Get(nameof(Settings_HotkeyStartStopTooltip));
    public static string Settings_HotkeyPauseResumeTooltip => Get(nameof(Settings_HotkeyPauseResumeTooltip));
    public static string Settings_HotkeyScreenshotTooltip => Get(nameof(Settings_HotkeyScreenshotTooltip));
    public static string Settings_NextProfile => Get(nameof(Settings_NextProfile));
    public static string Settings_HotkeyNextProfileTooltip => Get(nameof(Settings_HotkeyNextProfileTooltip));
    public static string Settings_MuteMic => Get(nameof(Settings_MuteMic));
    public static string Settings_HotkeyMuteMicTooltip => Get(nameof(Settings_HotkeyMuteMicTooltip));
    public static string Settings_DownloadPortable => Get(nameof(Settings_DownloadPortable));
    public static string Settings_DownloadPortableTooltip => Get(nameof(Settings_DownloadPortableTooltip));
    public static string Settings_UpdateRestart => Get(nameof(Settings_UpdateRestart));
    public static string Settings_UpdateRestartTooltip => Get(nameof(Settings_UpdateRestartTooltip));
    public static string Settings_CheckNow => Get(nameof(Settings_CheckNow));
    public static string Settings_CheckNowTooltip => Get(nameof(Settings_CheckNowTooltip));

    public static string Library_Videos => Get(nameof(Library_Videos));
    public static string Library_Screenshots => Get(nameof(Library_Screenshots));
    public static string Library_Open => Get(nameof(Library_Open));
    public static string Library_Play => Get(nameof(Library_Play));
    public static string Library_Reveal => Get(nameof(Library_Reveal));
    public static string Library_Delete => Get(nameof(Library_Delete));
    public static string Library_RecordAgain => Get(nameof(Library_RecordAgain));
    public static string Library_OpenFolder => Get(nameof(Library_OpenFolder));
    public static string Library_Refresh => Get(nameof(Library_Refresh));
    public static string Library_NoVideos => Get(nameof(Library_NoVideos));
    public static string Library_NoScreenshots => Get(nameof(Library_NoScreenshots));
    public static string Library_Loading => Get(nameof(Library_Loading));
    public static string Schedule_Title => Get(nameof(Schedule_Title));
    public static string Schedule_New => Get(nameof(Schedule_New));
    public static string Schedule_Subtext => Get(nameof(Schedule_Subtext));
    public static string Schedule_Edit => Get(nameof(Schedule_Edit));
    public static string Schedule_Delete => Get(nameof(Schedule_Delete));
    public static string Schedule_FollowRecordSettings => Get(nameof(Schedule_FollowRecordSettings));
    public static string Schedule_ProfileLabel => Get(nameof(Schedule_ProfileLabel));
    public static string ScheduleEdit_Title => Get(nameof(ScheduleEdit_Title));
    public static string ScheduleEdit_Name => Get(nameof(ScheduleEdit_Name));
    public static string ScheduleEdit_Recurrence => Get(nameof(ScheduleEdit_Recurrence));
    public static string ScheduleEdit_WeeklyDay => Get(nameof(ScheduleEdit_WeeklyDay));
    public static string ScheduleEdit_Time => Get(nameof(ScheduleEdit_Time));
    public static string ScheduleEdit_Duration => Get(nameof(ScheduleEdit_Duration));
    public static string ScheduleEdit_Profile => Get(nameof(ScheduleEdit_Profile));
    public static string ScheduleEdit_Save => Get(nameof(ScheduleEdit_Save));
    public static string ScheduleEdit_Cancel => Get(nameof(ScheduleEdit_Cancel));
    public static string ScheduleEdit_InvalidTime => Get(nameof(ScheduleEdit_InvalidTime));
    public static string Schedule_NoItems => Get(nameof(Schedule_NoItems));

    public static string Record_Profile => Get(nameof(Record_Profile));
    public static string Profile_Custom => Get(nameof(Profile_Custom));
    public static string Profile_SaveAs => Get(nameof(Profile_SaveAs));
    public static string Profile_Delete => Get(nameof(Profile_Delete));
    public static string Profile_SaveTitle => Get(nameof(Profile_SaveTitle));
    public static string Profile_Name => Get(nameof(Profile_Name));
    public static string Profile_InvalidName => Get(nameof(Profile_InvalidName));

    public static string About_Description => Get(nameof(About_Description));
    public static string About_PrivacyTitle => Get(nameof(About_PrivacyTitle));
    public static string About_PrivacyBody => Get(nameof(About_PrivacyBody));
    public static string About_ViewOnGitHub => Get(nameof(About_ViewOnGitHub));
    public static string About_License => Get(nameof(About_License));
    public static string About_ThirdPartyNotices => Get(nameof(About_ThirdPartyNotices));
    public static string About_CheckForUpdates => Get(nameof(About_CheckForUpdates));

    public static string About_Links => Get(nameof(About_Links));
    public static string About_GitHubHeader => Get(nameof(About_GitHubHeader));
    public static string About_GitHubTooltip => Get(nameof(About_GitHubTooltip));
    public static string About_LicenseTooltip => Get(nameof(About_LicenseTooltip));
    public static string About_ThirdPartyNoticesTooltip => Get(nameof(About_ThirdPartyNoticesTooltip));

    public static string App_Name => Get(nameof(App_Name));
    public static string Shell_ViewInLibraryTooltip => Get(nameof(Shell_ViewInLibraryTooltip));
    public static string Shell_ToggleThemeTooltip => Get(nameof(Shell_ToggleThemeTooltip));
    public static string Shell_Minimize => Get(nameof(Shell_Minimize));
    public static string Shell_Maximize => Get(nameof(Shell_Maximize));
    public static string Shell_Restore => Get(nameof(Shell_Restore));
    public static string Shell_Close => Get(nameof(Shell_Close));
    public static string Shell_Dismiss => Get(nameof(Shell_Dismiss));

    public static string Library_VideosTooltip => Get(nameof(Library_VideosTooltip));
    public static string Library_ScreenshotsTooltip => Get(nameof(Library_ScreenshotsTooltip));
    public static string Library_OpenFolderTooltip => Get(nameof(Library_OpenFolderTooltip));
    public static string Library_RefreshTooltip => Get(nameof(Library_RefreshTooltip));
    public static string Library_DeletePermanentTitle => Get(nameof(Library_DeletePermanentTitle));
    public static string Library_DeletePermanentBody => Get(nameof(Library_DeletePermanentBody));

    public static string Schedule_NewTooltip => Get(nameof(Schedule_NewTooltip));

    public static string Compact_Expand => Get(nameof(Compact_Expand));
    public static string Compact_ExpandTooltip => Get(nameof(Compact_ExpandTooltip));
    public static string Compact_WindowToRecordName => Get(nameof(Compact_WindowToRecordName));
    public static string Compact_Pick => Get(nameof(Compact_Pick));
    public static string Compact_SystemLabel => Get(nameof(Compact_SystemLabel));
    public static string Compact_SystemAudioTooltip => Get(nameof(Compact_SystemAudioTooltip));
    public static string Compact_SystemAudioName => Get(nameof(Compact_SystemAudioName));
    public static string Compact_MicLabel => Get(nameof(Compact_MicLabel));
    public static string Compact_MicTooltip => Get(nameof(Compact_MicTooltip));
    public static string Compact_MicName => Get(nameof(Compact_MicName));

    public static string Toolbar_DrawTooltip => Get(nameof(Toolbar_DrawTooltip));
    public static string Toolbar_ZoomTooltip => Get(nameof(Toolbar_ZoomTooltip));
    public static string Toolbar_MuteMicTooltip => Get(nameof(Toolbar_MuteMicTooltip));
    public static string Toolbar_StopTooltip => Get(nameof(Toolbar_StopTooltip));

    public static string RegionSelect_Instructions => Get(nameof(RegionSelect_Instructions));
    public static string RegionSelect_Preset1080 => Get(nameof(RegionSelect_Preset1080));
    public static string RegionSelect_Preset720 => Get(nameof(RegionSelect_Preset720));
    public static string RegionSelect_Full => Get(nameof(RegionSelect_Full));
    public static string RegionSelect_UseRegion => Get(nameof(RegionSelect_UseRegion));
    public static string Common_Cancel => Get(nameof(Common_Cancel));

    public static string WindowPicker_Instructions => Get(nameof(WindowPicker_Instructions));

    public static string Annotation_DrawingOnScreen => Get(nameof(Annotation_DrawingOnScreen));
    public static string Annotation_ExitDrawMode => Get(nameof(Annotation_ExitDrawMode));
    public static string Annotation_ExitDrawModeTooltip => Get(nameof(Annotation_ExitDrawModeTooltip));

    public static string Countdown_PressEscToCancel => Get(nameof(Countdown_PressEscToCancel));

    public static string ScheduleEdit_Date => Get(nameof(ScheduleEdit_Date));

    public static string Tray_ShowRecMode => Get(nameof(Tray_ShowRecMode));
    public static string Tray_StartStopRecording => Get(nameof(Tray_StartStopRecording));
    public static string Tray_Recent => Get(nameof(Tray_Recent));
    public static string Tray_Quit => Get(nameof(Tray_Quit));

    public static string Record_StatusReady => Get(nameof(Record_StatusReady));
    public static string Record_StatusStarting => Get(nameof(Record_StatusStarting));
    public static string Record_StatusRecording => Get(nameof(Record_StatusRecording));
    public static string Record_StatusFinalizing => Get(nameof(Record_StatusFinalizing));
}
