using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RecMode.App.Resources;

namespace RecMode.App.ViewModels;

/// <summary>One recording or screenshot shown in the Library list (plan Phase 5 — basic, filesystem-backed).</summary>
public sealed class LibraryItem : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private bool _thumbnailRequested;

    public required string FilePath { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Secondary line, e.g. "58 MB · Yesterday 14:12".</summary>
    public required string Meta { get; init; }

    /// <summary>True for screenshots (a real thumbnail is loaded); false for videos (a play badge is shown).</summary>
    public required bool IsImage { get; init; }

    /// <summary>Chapter titles this recording carries, from the library index (null/empty when it has none).
    /// The real chapters are written into the file itself, so this is the Library's jump-list reference —
    /// any player can seek them, and the tooltip lists the titles without needing an in-app player.</summary>
    public IReadOnlyList<string>? Chapters { get; init; }

    public bool HasChapters => Chapters is { Count: > 0 };

    public string ChaptersBadge => Chapters is { Count: > 0 } c
        ? $"{c.Count} chapter{(c.Count == 1 ? "" : "s")}"
        : "";

    public string ChaptersText => Chapters is { Count: > 0 } c ? string.Join(" · ", c) : "";

    /// <summary>
    /// Lazily decoded on first read, not eagerly for every item in the folder. <see cref="LibraryViewModel"/>
    /// used to decode a 160px thumbnail for every screenshot up front, in <c>BuildItems</c>, before the list
    /// was even shown — which defeated the whole point of the ListBox's virtualization: a folder of ~500
    /// screenshots (F11 is a one-key hotkey; this accumulates fast) meant ~500 full-resolution image decodes
    /// (WPF decodes at native size before scaling down) and ~500 frozen bitmaps retained in memory for as
    /// long as the Library page stayed open, regardless of how many were ever actually scrolled into view.
    /// <para>
    /// This getter is what WPF's data binding calls when — and only when — virtualization actually realizes
    /// this item's container. The first read kicks off an async decode and returns null immediately (WPF
    /// shows nothing until the image arrives, same as any async-image pattern); the decode runs off the UI
    /// thread and marshals back via <see cref="PropertyChanged"/>, which the `Image.Source` binding picks up
    /// automatically. Scrolling an item off-screen and back does NOT re-decode — <see cref="_thumbnailRequested"/>
    /// latches after the first read, whether it succeeded or not (a failed/corrupt image stays blank, not
    /// retried every scroll).
    /// </para>
    /// </summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (!IsImage || _thumbnailRequested)
            {
                return _thumbnail;
            }

            _thumbnailRequested = true;
            string path = FilePath;
            System.Threading.Tasks.Task.Run(() => TryLoadThumbnail(path)).ContinueWith(t =>
            {
                if (t.Result is not { } bmp)
                {
                    return;
                }

                // Marshal back explicitly rather than via TaskScheduler.FromCurrentSynchronizationContext —
                // matches this app's existing convention for background-thread-to-UI handoffs (e.g. the
                // global input hooks) and doesn't depend on a SynchronizationContext having been captured at
                // the moment this getter happened to run.
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _thumbnail = bmp;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
                });
            }, System.Threading.Tasks.TaskScheduler.Default);

            return null;
        }
    }

    /// <summary>Capture-source metadata, when indexed (videos only — see <see cref="RecMode.Core.Library.ILibraryIndex"/>).
    /// Drives the "Record again" action.</summary>
    public RecMode.Core.Library.LibraryIndexEntry? IndexEntry { get; init; }

    /// <summary>"Record again" only makes sense for indexed videos — there's nothing to re-apply otherwise.</summary>
    public bool CanRecordAgain => !IsImage && IndexEntry is not null;

    /// <summary>"Play" for videos (matches what the action actually does), "Open" for screenshots.</summary>
    public string OpenLabel => IsImage ? Strings.Library_Open : Strings.Library_Play;

    /// <summary>Tooltip/accessible name for the open/play button, e.g. "Play MyRecording" or "Open MyShot".</summary>
    public string OpenTooltip => $"{OpenLabel} {DisplayName}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private static ImageSource? TryLoadThumbnail(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // load now, don't lock the file
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = 160; // thumbnail-sized decode (hot-path friendly)
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze(); // makes it safe to hand to the UI thread from this background decode
            return bmp;
        }
        catch
        {
            return null; // unreadable/corrupt image — just skip the thumbnail
        }
    }
}
