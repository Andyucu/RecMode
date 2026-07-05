using System.Globalization;
using System.Windows.Data;

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
