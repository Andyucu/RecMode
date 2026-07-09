using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.FileIO;
using RecMode.App.Resources;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;

namespace RecMode.App.ViewModels;

/// <summary>
/// Basic, filesystem-backed Library (plan Phase 5 MVP cut): lists recordings and screenshots from the output
/// folders with open / reveal-in-Explorer / delete-to-Recycle-Bin. Loaded on navigation (§3.9 — no work when
/// the page isn't visible). The richer metadata index (codec/res/duration, tags) is a later Library-pro pass.
/// </summary>
public sealed class LibraryViewModel : ObservableObject, INavigationAware
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".mov", ".webm"];
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    private readonly IAppPaths _paths;
    private readonly IErrorReporter _errors;
    private readonly RecMode.Core.Library.ILibraryIndex _index;
    private bool _showVideos = true;

    public LibraryViewModel(IAppPaths paths, IErrorReporter errors, RecMode.Core.Library.ILibraryIndex index)
    {
        _paths = paths;
        _errors = errors;
        _index = index;

        ShowVideosCommand = new RelayCommand(() => SetTab(videos: true));
        ShowScreenshotsCommand = new RelayCommand(() => SetTab(videos: false));
        RefreshCommand = new RelayCommand(Load);
        OpenFolderCommand = new RelayCommand(OpenCurrentFolder);
        OpenCommand = new RelayCommand<LibraryItem>(Open);
        RevealCommand = new RelayCommand<LibraryItem>(Reveal);
        DeleteCommand = new RelayCommand<LibraryItem>(Delete);
    }

    public ObservableCollection<LibraryItem> Items { get; } = [];

    public IRelayCommand ShowVideosCommand { get; }
    public IRelayCommand ShowScreenshotsCommand { get; }
    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand<LibraryItem> OpenCommand { get; }
    public IRelayCommand<LibraryItem> RevealCommand { get; }
    public IRelayCommand<LibraryItem> DeleteCommand { get; }

    public bool ShowVideos
    {
        get => _showVideos;
        private set
        {
            if (SetProperty(ref _showVideos, value))
            {
                OnPropertyChanged(nameof(ShowScreenshots));
                OnPropertyChanged(nameof(EmptyMessage));
            }
        }
    }

    public bool ShowScreenshots => !_showVideos;
    public bool IsEmpty => Items.Count == 0;
    public string EmptyMessage => _showVideos ? Strings.Library_NoVideos : Strings.Library_NoScreenshots;

    public void OnNavigatedTo() => Load();

    public void OnNavigatedFrom() => Items.Clear();

    private void SetTab(bool videos)
    {
        if (ShowVideos != videos)
        {
            ShowVideos = videos;
            Load();
        }
    }

    private void Load()
    {
        Items.Clear();

        string dir = _showVideos ? _paths.RecordingsDirectory : _paths.ScreenshotsDirectory;
        if (Directory.Exists(dir))
        {
            string[] exts = _showVideos ? VideoExtensions : ImageExtensions;
            IReadOnlyDictionary<string, RecMode.Core.Library.LibraryIndexEntry> meta =
                _showVideos ? _index.ByFileName() : new Dictionary<string, RecMode.Core.Library.LibraryIndexEntry>();

            IEnumerable<FileInfo> files = new DirectoryInfo(dir).EnumerateFiles()
                .Where(f => exts.Contains(f.Extension.ToLowerInvariant()))
                .Where(f => !f.Name.EndsWith(".recording.mkv", StringComparison.OrdinalIgnoreCase)) // skip in-progress temp
                .OrderByDescending(f => f.LastWriteTime);

            foreach (FileInfo f in files)
            {
                Items.Add(new LibraryItem
                {
                    FilePath = f.FullName,
                    DisplayName = Path.GetFileNameWithoutExtension(f.Name),
                    Meta = BuildMeta(f, meta.GetValueOrDefault(f.Name)),
                    IsImage = !_showVideos,
                    Thumbnail = _showVideos ? null : TryLoadThumbnail(f.FullName),
                });
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Open(LibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        Run(() => Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true }),
            "library.open-failed", "Couldn't open the file.");
    }

    private void Reveal(LibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        Run(() => Process.Start("explorer.exe", $"/select,\"{item.FilePath}\""),
            "library.reveal-failed", "Couldn't show the file in Explorer.");
    }

    private void Delete(LibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            // Recycle Bin, not permanent — a mis-click is recoverable.
            FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            Items.Remove(item);
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            _errors.Warn("library.delete-failed", "Couldn't delete the file.", null, ex);
        }
    }

    private void OpenCurrentFolder()
    {
        string dir = _showVideos ? _paths.RecordingsDirectory : _paths.ScreenshotsDirectory;
        Directory.CreateDirectory(dir);
        Run(() => Process.Start("explorer.exe", $"\"{dir}\""),
            "library.folder-failed", "Couldn't open the folder.");
    }

    private void Run(Action action, string code, string message)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _errors.Warn(code, message, null, ex);
        }
    }

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
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null; // unreadable/corrupt image — just skip the thumbnail
        }
    }

    /// <summary>"H.264 · 1920×1080 · 0:12 · 58 MB · Today 14:12" when indexed; "size · date" otherwise. Internal
    /// (rather than private), along with its helpers below, so they're directly unit-testable.</summary>
    internal static string BuildMeta(FileInfo f, RecMode.Core.Library.LibraryIndexEntry? entry)
    {
        string tail = $"{FormatSize(f.Length)} · {FormatDate(f.LastWriteTime)}";
        if (entry is null)
        {
            return tail;
        }

        string codec = FriendlyCodec(entry.Codec);
        return $"{codec} · {entry.Width}×{entry.Height} · {FormatDuration(entry.DurationSeconds)} · {tail}";
    }

    internal static string FriendlyCodec(string codec) => codec switch
    {
        "H264" => "H.264",
        "Hevc" => "HEVC",
        "Av1" => "AV1",
        _ => codec,
    };

    internal static string FormatDuration(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F0} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };

    internal static string FormatDate(DateTime when)
    {
        DateTime today = DateTime.Today;
        if (when.Date == today)
        {
            return $"Today {when:HH:mm}";
        }

        if (when.Date == today.AddDays(-1))
        {
            return $"Yesterday {when:HH:mm}";
        }

        return when.ToString("yyyy-MM-dd HH:mm");
    }
}
