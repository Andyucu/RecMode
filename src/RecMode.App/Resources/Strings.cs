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
    public static string Record_Start => Get(nameof(Record_Start));
    public static string Record_Stop => Get(nameof(Record_Stop));
    public static string Record_Screenshot => Get(nameof(Record_Screenshot));
    public static string Status_Ready => Get(nameof(Status_Ready));
    public static string Status_Recording => Get(nameof(Status_Recording));

    public static string Settings_Appearance => Get(nameof(Settings_Appearance));
    public static string Settings_Theme => Get(nameof(Settings_Theme));
    public static string Settings_Accent => Get(nameof(Settings_Accent));
    public static string Settings_Output => Get(nameof(Settings_Output));
    public static string Settings_Browse => Get(nameof(Settings_Browse));

    public static string Library_Empty => Get(nameof(Library_Empty));
    public static string Schedule_Empty => Get(nameof(Schedule_Empty));
}
