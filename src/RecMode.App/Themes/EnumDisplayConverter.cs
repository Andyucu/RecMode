using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RecMode.App.Themes;

/// <summary>
/// One-way converter turning enum values into their design-facing labels (e.g. <c>Av1</c> → "AV1",
/// <c>Mp4</c> → "MP4", <c>WebM</c> → "WebM"). Used as the display for the encoding-defaults and Record combos
/// so the UI reads like the design while the stored value stays a plain enum. Unmapped values fall back to
/// <c>ToString()</c>.
/// </summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        // Video codecs
        ["H264"] = "H.264",
        ["Hevc"] = "HEVC",
        ["Av1"] = "AV1",
        // Containers
        ["Mp4"] = "MP4",
        ["Mkv"] = "MKV",
        ["Mov"] = "MOV",
        ["WebM"] = "WebM",
        // Audio codecs
        ["Aac"] = "AAC",
        ["Opus"] = "Opus",
        ["Flac"] = "FLAC",
        // Theme
        ["System"] = "System",
        ["Light"] = "Light",
        ["Dark"] = "Dark",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        string key = value.ToString() ?? "";
        return Labels.TryGetValue(key, out string? label) ? label : key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Visible when the bound enum equals the <c>ConverterParameter</c> (member name), else Collapsed. One-way.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

/// <summary>Shows the CPU thread cap: 0 → "Auto", otherwise the number. One-way (the combo edits the int directly).</summary>
public sealed class ThreadCapConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "Auto";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Shows an auto-split size in MB as a friendly "~3.9 GB" label. One-way (the combo edits the int MB directly).</summary>
public sealed class AutoSplitSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int mb ? $"~{mb / 1024.0:0.#} GB" : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Audio meter fill colour: the design's caution rule (RecMode.dc.html meterTick) — turn the bar
/// caution-coloured once the level exceeds 82%, accent-coloured otherwise. Bound directly to the 0..1 RMS
/// meter value so it re-evaluates on every meter tick (naturally theme-safe: it re-resolves the current
/// theme's brush via <c>Application.Current</c> each time rather than caching a stale reference).
/// </summary>
public sealed class MeterCautionConverter : IValueConverter
{
    public const double CautionThreshold = 0.82;

    public static bool IsCaution(double level) => level > CautionThreshold;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is double level && IsCaution(level) ? "CautionBrush" : "AccentBrush";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
