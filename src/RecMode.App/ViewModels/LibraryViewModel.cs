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
    private LibraryItem? _selectedItem;
    private string? _pendingSelectPath;

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

    /// <summary>The item to highlight/scroll to, driven by <see cref="RequestSelect"/> — bound two-way to the
    /// Library's ListBox so selecting it there also updates this (harmless; nothing currently reads that
    /// direction back).</summary>
    public LibraryItem? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    /// <summary>Jump to a specific recording or screenshot next time the Library loads — the title bar's
    /// "Saved &lt;filename&gt;"/"Screenshot saved" status uses this to point straight at the file that just
    /// finished (see <see cref="ShellViewModel.OpenLastRecordingCommand"/>). Switches to whichever tab
    /// actually holds that file — a screenshot's own extension (.png) is how it's told apart from a
    /// recording, since callers only ever pass a path, not a kind.</summary>
    public void RequestSelect(string filePath)
    {
        _pendingSelectPath = filePath;
        ShowVideos = !string.Equals(Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase);
    }

    public void OnNavigatedTo() => _ = LoadAsync();

    public void OnNavigatedFrom()
    {
        // Dispose as well as cancel. LoadAsync disposes the *previous* CTS on each new load, so without this
        // the last one leaks its timer/handle registration for the lifetime of this singleton view model —
        // i.e. until the user happens to visit Library again.
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        Items.Clear();
        SelectedItem = null;
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
        SelectedItem = null;
        OnPropertyChanged(nameof(IsEmpty));
        IsLoading = true;

        string dir = CurrentDirectory();
        bool videos = _showVideos;
        try
        {
            // Building each LibraryItem happens entirely inside this background Task.Run, not just the file
            // scan — the metadata build (BuildMeta, library-index lookup) is cheap but still shouldn't run on
            // the UI thread for hundreds of files. Thumbnail decoding itself is handled separately and
            // lazily by LibraryItem.Thumbnail's own getter (see its doc comment) — it used to happen eagerly
            // right here for every screenshot, which defeated the ListBox's own VirtualizingPanel (declared
            // in LibraryView.xaml): every item was decoded and retained up front regardless of how many were
            // ever actually scrolled into view. A user with ~500 screenshots (F11 is a one-key hotkey; this
            // accumulates fast) held tens of MB of decoded bitmaps for the whole time Library stayed open.
            var items = await Task.Run(() => BuildItems(dir, videos, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(cancellation, _loadCancellation)) return;

            foreach (LibraryItem item in items)
            {
                Items.Add(item);
            }

            if (_pendingSelectPath is { } selectPath)
            {
                SelectedItem = Items.FirstOrDefault(i => string.Equals(i.FilePath, selectPath, StringComparison.OrdinalIgnoreCase));
                _pendingSelectPath = null;
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

    /// <summary>Enumerates matching files, or returns <c>null</c> when the directory itself is unavailable
    /// (disconnected drive/share, or an <c>OutputFolder</c> that no longer exists). That distinction matters:
    /// "unavailable" must NOT be conflated with "empty", because <see cref="BuildItems"/> prunes the library
    /// index against this result — see the guard there.</summary>
    private static List<FileInfo>? ScanFiles(string directory, bool videos, CancellationToken ct)
    {
        if (!Directory.Exists(directory)) return null;
        string[] extensions = videos ? VideoExtensions : ImageExtensions;
        return new DirectoryInfo(directory).EnumerateFiles()
            .TakeWhile(_ => !ct.IsCancellationRequested)
            .Where(f => extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Name.EndsWith(".recording.mkv", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    /// <summary>Scans the directory and builds every <see cref="LibraryItem"/> — thumbnail decode included —
    /// entirely off the UI thread. See the call site in <see cref="LoadAsync"/> for why this is safe.</summary>
    private List<LibraryItem> BuildItems(string directory, bool videos, CancellationToken ct)
    {
        List<FileInfo>? scanned = ScanFiles(directory, videos, ct);
        List<FileInfo> files = scanned ?? [];

        // Only prune when the directory genuinely exists AND was fully enumerated. Two distinct ways a
        // non-authoritative file list could otherwise destroy the whole index:
        //   1. Directory unavailable (scanned is null) — a disconnected USB/network drive, or an OutputFolder
        //      the user just repointed. ScanFiles used to return an empty list here, indistinguishable from
        //      "the folder is genuinely empty", so PruneMissing removed EVERY entry and persisted it. Every
        //      recording permanently lost its codec/resolution/duration metadata and its "Record again"
        //      action, silently — LoadAsync's folder-unavailable warning never fires for this case, because
        //      Directory.Exists returns false rather than throwing.
        //   2. Partial/cancelled scan — EnumerateFiles is deliberately cancellable, so a navigation or tab
        //      switch can stop it after only a prefix of the directory (pre-existing guard, kept).
        if (videos && scanned is not null)
        {
            ct.ThrowIfCancellationRequested();
            _index.PruneMissing(new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase), directory);
        }
        IReadOnlyDictionary<string, RecMode.Core.Library.LibraryIndexEntry> meta =
            videos ? _index.ByFileName() : new Dictionary<string, RecMode.Core.Library.LibraryIndexEntry>();

        var items = new List<LibraryItem>(files.Count);
        foreach (FileInfo f in files)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            RecMode.Core.Library.LibraryIndexEntry? entry = meta.GetValueOrDefault(f.Name);
            items.Add(new LibraryItem
            {
                FilePath = f.FullName,
                DisplayName = Path.GetFileNameWithoutExtension(f.Name),
                Meta = BuildMeta(f, entry),
                IsImage = !videos,
                Chapters = entry?.Chapters,
                // Thumbnail is no longer set here — LibraryItem.Thumbnail lazily decodes on its own first
                // read, i.e. only once virtualization actually realizes this item's container. See its doc
                // comment for why eager decoding here defeated the point of a virtualized list.
                IndexEntry = entry,
            });
        }

        return items;
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

    // Process.Start("explorer.exe", ...) with the default UseShellExecute=false resolves a bare filename via
    // CreateProcess's own search order, which checks the *launching process's own directory first* — a
    // portable, self-contained app, so that directory is exactly wherever the user unzipped RecMode.exe. A
    // file named explorer.exe planted next to it would run instead of the real one. Fully qualifying the path
    // to %WINDIR%\explorer.exe (the one location that's actually genuine) closes that off.
    private static readonly string ExplorerExePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    private void Reveal(LibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        Run(() => Process.Start(ExplorerExePath, $"/select,\"{item.FilePath}\""),
            "library.reveal-failed", "Couldn't show the file in Explorer.");
    }

    private void Delete(LibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        // Windows keeps no Recycle Bin on removable or network volumes, so the shell silently falls back to a
        // permanent delete there — and UIOption.OnlyErrorDialogs (FOF_SILENT | FOF_NOCONFIRMATION) suppresses
        // the "this can't be recycled, delete permanently?" prompt that would otherwise be the user's only
        // warning. That's the *default* configuration for this app, not an edge case: portable installs keep
        // .\Recordings inside the app folder, so running from a USB stick put every recording on removable
        // media. A mis-click destroyed it outright while the button's own tooltip promised the Recycle Bin.
        // Confirm explicitly when the delete genuinely can't be undone.
        if (!CanRecycle(item.FilePath) &&
            System.Windows.MessageBox.Show(
                $"{Path.GetFileName(item.FilePath)}\n\n{Resources.Strings.Library_DeletePermanentBody}",
                Resources.Strings.Library_DeletePermanentTitle,
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.Cancel) != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            // Recycle Bin where the volume supports one; permanent (confirmed above) where it doesn't.
            FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            if (item.IsImage == false)
            {
                _index.Remove(Path.GetFileName(item.FilePath));
                DeleteTranscriptSidecars(item.FilePath);
            }
            Items.Remove(item);
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            _errors.Warn("library.delete-failed", "Couldn't delete the file.", null, ex);
        }
    }

    /// <summary>Removes the caption sidecars a transcription wrote beside a recording. They carry a different
    /// extension, so the Library's own scan never lists them — without this they would sit in the recordings
    /// folder forever after their recording is gone, invisible in the app and puzzling in Explorer. Best
    /// effort: a sidecar that can't be removed is clutter, never a reason to fail the delete the user asked
    /// for (the recording itself is already gone by this point).</summary>
    private static void DeleteTranscriptSidecars(string mediaPath)
    {
        foreach (string sidecar in new[]
                 {
                     Services.Transcripts.ITranscriptionService.SrtPathFor(mediaPath),
                     Services.Transcripts.ITranscriptionService.VttPathFor(mediaPath),
                 })
        {
            try
            {
                if (File.Exists(sidecar))
                {
                    FileSystem.DeleteFile(sidecar, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
            }
        }
    }

    /// <summary>True when the file's volume actually has a Recycle Bin, so a delete is undoable. Fixed
    /// (internal/SATA/NVMe) drives do; removable and network ones don't. Fails safe: anything unreadable is
    /// treated as non-recyclable, which asks for confirmation rather than silently destroying the file.</summary>
    private static bool CanRecycle(string filePath)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(filePath));
            return root is not null && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
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
            Process.Start(ExplorerExePath, $"\"{dir}\"");
        },
            "library.folder-failed", "Couldn't open the folder.");
    }

    private string CurrentDirectory() => _showVideos
        ? _paths.ResolveUserPath(_settings.Current.OutputFolder) ?? _paths.RecordingsDirectory
        : _paths.ResolveUserPath(_settings.Current.ScreenshotFolder) ?? _paths.ScreenshotsDirectory;

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
