using System.Windows.Media;
using RecMode.App.Resources;

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

    /// <summary>Capture-source metadata, when indexed (videos only — see <see cref="RecMode.Core.Library.ILibraryIndex"/>).
    /// Drives the "Record again" action.</summary>
    public RecMode.Core.Library.LibraryIndexEntry? IndexEntry { get; init; }

    /// <summary>"Record again" only makes sense for indexed videos — there's nothing to re-apply otherwise.</summary>
    public bool CanRecordAgain => !IsImage && IndexEntry is not null;

    /// <summary>"Play" for videos (matches what the action actually does), "Open" for screenshots.</summary>
    public string OpenLabel => IsImage ? Strings.Library_Open : Strings.Library_Play;

    /// <summary>Tooltip/accessible name for the open/play button, e.g. "Play MyRecording" or "Open MyShot".</summary>
    public string OpenTooltip => $"{OpenLabel} {DisplayName}";
}
