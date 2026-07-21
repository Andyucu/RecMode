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

    public static string AppTitle => Get(nameof(AppTitle));

    public static string Nav_Record => Get(nameof(Nav_Record));
    public static string Nav_Library => Get(nameof(Nav_Library));
    public static string Nav_Schedule => Get(nameof(Nav_Schedule));
    public static string Nav_Settings => Get(nameof(Nav_Settings));

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
    public static string Record_Start => Get(nameof(Record_Start));
    public static string Record_Stop => Get(nameof(Record_Stop));
    public static string Record_Screenshot => Get(nameof(Record_Screenshot));
    public static string Record_Annotate => Get(nameof(Record_Annotate));
    public static string Status_Ready => Get(nameof(Status_Ready));
    public static string Status_Recording => Get(nameof(Status_Recording));

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
    public static string Settings_SaveTo => Get(nameof(Settings_SaveTo));
    public static string Settings_Pattern => Get(nameof(Settings_Pattern));
    public static string Settings_PatternDesc => Get(nameof(Settings_PatternDesc));
    public static string Settings_Countdown => Get(nameof(Settings_Countdown));
    public static string Settings_CountdownDesc => Get(nameof(Settings_CountdownDesc));
    public static string Settings_Cursor => Get(nameof(Settings_Cursor));
    public static string Settings_CursorDesc => Get(nameof(Settings_CursorDesc));
    public static string Settings_Clicks => Get(nameof(Settings_Clicks));
    public static string Settings_ClicksDesc => Get(nameof(Settings_ClicksDesc));
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
    public static string Settings_Updates => Get(nameof(Settings_Updates));
    public static string Settings_License => Get(nameof(Settings_License));
    public static string Settings_LicenseDesc => Get(nameof(Settings_LicenseDesc));

    public static string Library_Empty => Get(nameof(Library_Empty));
    public static string Library_Videos => Get(nameof(Library_Videos));
    public static string Library_Screenshots => Get(nameof(Library_Screenshots));
    public static string Library_Open => Get(nameof(Library_Open));
    public static string Library_Reveal => Get(nameof(Library_Reveal));
    public static string Library_Delete => Get(nameof(Library_Delete));
    public static string Library_RecordAgain => Get(nameof(Library_RecordAgain));
    public static string Library_OpenFolder => Get(nameof(Library_OpenFolder));
    public static string Library_Refresh => Get(nameof(Library_Refresh));
    public static string Library_NoVideos => Get(nameof(Library_NoVideos));
    public static string Library_NoScreenshots => Get(nameof(Library_NoScreenshots));
    public static string Schedule_Empty => Get(nameof(Schedule_Empty));
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
    public static string Profile_NameTaken => Get(nameof(Profile_NameTaken));
}
