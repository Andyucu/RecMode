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
using RecMode.Core.Settings;

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
    private readonly ISettingsService _settings;
    private readonly IErrorReporter _errors;
    private readonly RecMode.Core.Library.ILibraryIndex _index;
    private readonly RecordViewModel _record;
    private bool _showVideos = true;
    private CancellationTokenSource? _loadCancellation;
    private bool _isLoading;

    public LibraryViewModel(IAppPaths paths, ISettingsService settings, IErrorReporter errors, RecMode.Core.Library.ILibraryIndex index,
        RecordViewModel record)
    {
        _paths = paths;
        _settings = settings;
        _errors = errors;
        _index = index;
        _record = record;

        ShowVideosCommand = new RelayCommand(() => SetTab(videos: true));
        ShowScreenshotsCommand = new RelayCommand(() => SetTab(videos: false));
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenFolderCommand = new RelayCommand(OpenCurrentFolder);
        OpenCommand = new RelayCommand<LibraryItem>(Open);
        RevealCommand = new RelayCommand<LibraryItem>(Reveal);
        DeleteCommand = new RelayCommand<LibraryItem>(Delete);
        RecordAgainCommand = new RelayCommand<LibraryItem>(RecordAgain);
    }

    /// <summary>Raised after "Record again" applies its settings to <see cref="RecordViewModel"/>, so the shell
    /// can switch to the Record page — <see cref="LibraryViewModel"/> has no navigation concept of its own.</summary>
    public event Action? RecordAgainRequested;

    public ObservableCollection<LibraryItem> Items { get; } = [];

    public IRelayCommand ShowVideosCommand { get; }
    public IRelayCommand ShowScreenshotsCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand<LibraryItem> OpenCommand { get; }
    public IRelayCommand<LibraryItem> RevealCommand { get; }
    public IRelayCommand<LibraryItem> DeleteCommand { get; }
    public IRelayCommand<LibraryItem> RecordAgainCommand { get; }

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
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public string EmptyMessage => _showVideos ? Strings.Library_NoVideos : Strings.Library_NoScreenshots;

    public void OnNavigatedTo() => _ = LoadAsync();

    public void OnNavigatedFrom()
    {
        _loadCancellation?.Cancel();
        Items.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void SetTab(bool videos)
    {
        if (ShowVideos != videos)
        {
            ShowVideos = videos;
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        Items.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        IsLoading = true;

        string dir = CurrentDirectory();
        bool videos = _showVideos;
        try
        {
            var files = await Task.Run(() => ScanFiles(dir, videos, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(cancellation, _loadCancellation)) return;

            if (videos)
            {
                _index.PruneMissing(new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase));
            }
            IReadOnlyDictionary<string, RecMode.Core.Library.LibraryIndexEntry> meta =
                videos ? _index.ByFileName() : new Dictionary<string, RecMode.Core.Library.LibraryIndexEntry>();
            foreach (FileInfo f in files)
            {
                RecMode.Core.Library.LibraryIndexEntry? entry = meta.GetValueOrDefault(f.Name);
                Items.Add(new LibraryItem
                {
                    FilePath = f.FullName,
                    DisplayName = Path.GetFileNameWithoutExtension(f.Name),
                    Meta = BuildMeta(f, entry),
                    IsImage = !videos,
                    Thumbnail = videos ? null : TryLoadThumbnail(f.FullName),
                    IndexEntry = entry,
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _errors.Warn("library.folder-unavailable", "The recording folder is unavailable.", "Reconnect the shared folder and refresh the Library.", ex);
        }
        finally
        {
            if (ReferenceEquals(cancellation, _loadCancellation)) IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private static List<FileInfo> ScanFiles(string directory, bool videos, CancellationToken ct)
    {
        if (!Directory.Exists(directory)) return [];
        string[] extensions = videos ? VideoExtensions : ImageExtensions;
        return new DirectoryInfo(directory).EnumerateFiles()
            .TakeWhile(_ => !ct.IsCancellationRequested)
            .Where(f => extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Name.EndsWith(".recording.mkv", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
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
            if (item.IsImage == false)
            {
                _index.Remove(Path.GetFileName(item.FilePath));
            }
            Items.Remove(item);
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            _errors.Warn("library.delete-failed", "Couldn't delete the file.", null, ex);
        }
    }

    private void RecordAgain(LibraryItem? item)
    {
        if (item?.IndexEntry is not { } entry)
        {
            return;
        }

        _record.ApplyRecordAgainSettings(entry);
        RecordAgainRequested?.Invoke();
    }

    private void OpenCurrentFolder()
    {
        string dir = CurrentDirectory();
        Run(() =>
        {
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", $"\"{dir}\"");
        },
            "library.folder-failed", "Couldn't open the folder.");
    }

    private string CurrentDirectory() => _showVideos
        ? _settings.Current.OutputFolder ?? _paths.RecordingsDirectory
        : _settings.Current.ScreenshotFolder ?? _paths.ScreenshotsDirectory;

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
