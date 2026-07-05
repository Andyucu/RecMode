using System.Windows.Media;

namespace RecMode.App.ViewModels;

/// <summary>One recording or screenshot shown in the Library list (plan Phase 5 — basic, filesystem-backed).</summary>
public sealed class LibraryItem
{
    public required string FilePath { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Secondary line, e.g. "58 MB · Yesterday 14:12".</summary>
    public required string Meta { get; init; }

    /// <summary>True for screenshots (a real thumbnail is loaded); false for videos (a play badge is shown).</summary>
    public required bool IsImage { get; init; }

    /// <summary>Loaded thumbnail for screenshots; null for videos.</summary>
    public ImageSource? Thumbnail { get; init; }
}
