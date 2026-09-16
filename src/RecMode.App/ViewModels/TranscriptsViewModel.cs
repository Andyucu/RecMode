using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.App.Resources;
using RecMode.App.Services.Transcripts;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>One recording row on the Transcripts page.</summary>
public sealed partial class TranscriptRowViewModel : ObservableObject
{
    public required string FilePath { get; init; }
    public required string DisplayName { get; init; }
    public required string Meta { get; init; }
    public required bool HasTranscript { get; init; }

    private string _excerpt = "";
    /// <summary>First line of the transcript, or the line that matched the current search.</summary>
    public required string Excerpt
    {
        get => _excerpt;
        set => SetProperty(ref _excerpt, value);
    }

    /// <summary>Full transcript text — what search actually scans.</summary>
    public required string TranscriptText { get; init; }

    /// <summary>True while this row is being transcribed. A real observable property, not an auto-property:
    /// it drives the row's button state, and a silent one would leave the UI showing "Transcribe" throughout.</summary>
    private bool _isTranscribing;
    public bool IsTranscribing
    {
        get => _isTranscribing;
        set
        {
            if (SetProperty(ref _isTranscribing, value))
            {
                OnPropertyChanged(nameof(CanTranscribe));
            }
        }
    }

    /// <summary>Inverse of <see cref="IsTranscribing"/>, for binding the row button's IsEnabled directly.</summary>
    public bool CanTranscribe => !_isTranscribing;
}

/// <summary>
/// Transcripts page (plan §7): every recording's local caption sidecar, with a search box over the spoken
/// words. The transcription itself never leaves the machine — see <see cref="ITranscriptionService"/>.
/// </summary>
public sealed partial class TranscriptsViewModel : ObservableObject, INavigationAware
{
    private readonly IAppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly IWhisperModelStore _models;
    private readonly ITranscriptionService _transcription;
    private readonly IErrorReporter _errors;

    public TranscriptsViewModel(IAppPaths paths, ISettingsService settings, IWhisperModelStore models,
        ITranscriptionService transcription, IErrorReporter errors)
    {
        _paths = paths;
        _settings = settings;
        _models = models;
        _transcription = transcription;
        _errors = errors;

        _selectedModel = settings.Current.TranscriptModel;
        TranscribeCommand = new RelayCommand<TranscriptRowViewModel>(row => _ = TranscribeAsync(row));
        DownloadModelCommand = new AsyncRelayCommand(DownloadModelAsync, () => !IsBusy && !IsModelReady);
        CancelDownloadCommand = new RelayCommand(() => _downloadCancellation?.Cancel(), () => IsDownloadingModel);
        OpenCommand = new RelayCommand<TranscriptRowViewModel>(Open);
        SaveTranscriptCommand = new RelayCommand<TranscriptRowViewModel>(SaveTranscript);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
    }

    public ObservableCollection<TranscriptRowViewModel> Items { get; } = [];
    public IRelayCommand<TranscriptRowViewModel> TranscribeCommand { get; }
    public IRelayCommand<TranscriptRowViewModel> OpenCommand { get; }

    /// <summary>Saves a transcript wherever the user wants it — plain text by default, or the standard
    /// caption files (.srt/.vtt) that already sit beside the recording. The sidecars exist so other tools can
    /// use the captions; this is for handing the words to someone as a file.</summary>
    public IRelayCommand<TranscriptRowViewModel> SaveTranscriptCommand { get; }
    public AsyncRelayCommand DownloadModelCommand { get; }

    /// <summary>Aborts an in-flight model download. Half a gigabyte with no way out is not an acceptable
    /// thing to start on someone's metered connection.</summary>
    public IRelayCommand CancelDownloadCommand { get; }

    private CancellationTokenSource? _downloadCancellation;
    public IRelayCommand ClearSearchCommand { get; }

    public IReadOnlyList<TranscriptModelSize> ModelSizes { get; } = [TranscriptModelSize.Tiny, TranscriptModelSize.Base, TranscriptModelSize.Small];

    private TranscriptModelSize _selectedModel;
    public TranscriptModelSize SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
            {
                _settings.Current.TranscriptModel = value;
                _settings.RequestSave();
                OnPropertyChanged(nameof(IsModelReady));
                OnPropertyChanged(nameof(ModelStatusText));
                DownloadModelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsModelReady => _models.IsAvailable(SelectedModel);

    public string ModelStatusText => IsModelReady
        ? Strings.Transcripts_ModelReady
        : Strings.Transcripts_ModelNeeded.Replace("{0}",
            (_models.ExpectedBytes(SelectedModel) / (1024 * 1024)).ToString(System.Globalization.CultureInfo.CurrentCulture),
            StringComparison.Ordinal);

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplySearch(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                DownloadModelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private bool _isDownloadingModel;
    public bool IsDownloadingModel
    {
        get => _isDownloadingModel;
        private set
        {
            if (SetProperty(ref _isDownloadingModel, value))
            {
                CancelDownloadCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private double _downloadProgress;
    public double DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }

    public string EmptyMessage => Items.Count == 0
        ? Strings.Transcripts_EmptyNoRecordings
        : Strings.Transcripts_EmptyNoMatches;

    public int TranscribedCount => Items.Count(i => i.HasTranscript);

    public void OnNavigatedTo() => _ = ReloadAsync();

    public void OnNavigatedFrom() { }

    public void Reload() => _ = ReloadAsync();

    /// <summary>Rebuilds the list off the UI thread. Every row opens its recording's <c>.srt</c> and parses
    /// it, so a folder of 300 recordings is 300 file reads plus 300 parses — doing that inline on navigation
    /// froze the window for as long as the disk took, and on a network or removable recordings folder that is
    /// seconds, not milliseconds. The Library page already scans this same folder off-thread for exactly this
    /// reason; this page was the odd one out. Only the collection update runs back on the UI thread.</summary>
    private async Task ReloadAsync()
    {
        string folder = _paths.RecordingsDirectory;
        List<TranscriptRowViewModel>? rows = null;
        bool failed = false;
        bool truncated = false;

        await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    rows = [];
                    return;
                }

                List<string> all = [.. Directory.EnumerateFiles(folder)
                    .Where(IsTranscribable)
                    .OrderByDescending(File.GetLastWriteTimeUtc)];
                truncated = all.Count > MaxRows;
                rows = [.. all.Take(MaxRows).Select(BuildRow)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed = true;
            }
        });

        if (failed)
        {
            _errors.Warn("transcript.scan-failed", Strings.Transcripts_ScanFailed);
        }

        Items.Clear();
        foreach (TranscriptRowViewModel row in rows ?? [])
        {
            Items.Add(row);
        }

        IsTruncated = truncated;
        ApplySearch();
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(TranscribedCount));
        OnPropertyChanged(nameof(IsModelReady));
        OnPropertyChanged(nameof(ModelStatusText));
    }

    /// <summary>Upper bound on rows built per visit. Each row reads and parses a whole <c>.srt</c>, so an
    /// unbounded list would make the page's cost grow with the recordings folder forever.</summary>
    private const int MaxRows = 300;

    private bool _isTruncated;

    /// <summary>True when the folder holds more recordings than <see cref="MaxRows"/>. Surfaced rather than
    /// silently swallowed: a user searching their transcripts needs to know the search didn't cover
    /// everything, or they will read "no matches" as "not said".</summary>
    public bool IsTruncated
    {
        get => _isTruncated;
        private set
        {
            if (SetProperty(ref _isTruncated, value))
            {
                OnPropertyChanged(nameof(TruncatedMessage));
            }
        }
    }

    public string TruncatedMessage =>
        Strings.Transcripts_ListTruncated.Replace("{0}",
            MaxRows.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal);

    private static bool IsTranscribable(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mkv" or ".mov" or ".webm";

    private static TranscriptRowViewModel BuildRow(string path)
    {
        var info = new FileInfo(path);
        TranscriptDocument? transcript = ITranscriptionService.LoadSidecar(path);
        string text = transcript?.PlainText ?? "";

        return new TranscriptRowViewModel
        {
            FilePath = path,
            DisplayName = Path.GetFileNameWithoutExtension(path),
            Meta = $"{Math.Round(info.Length / 1024.0 / 1024.0)} MB · {info.LastWriteTime:g}",
            HasTranscript = transcript is not null,
            TranscriptText = text,
            Excerpt = text.Length > 160 ? text[..160] + "…" : text,
        };
    }

    /// <summary>Filters the list to recordings whose transcript contains the query, and re-points each row's
    /// excerpt at the matching line so a hit is visible without opening anything.</summary>
    private void ApplySearch()
    {
        string query = _searchText.Trim();
        if (query.Length == 0)
        {
            foreach (TranscriptRowViewModel row in Items)
            {
                row.Excerpt = row.TranscriptText.Length > 160 ? row.TranscriptText[..160] + "…" : row.TranscriptText;
            }

            OnPropertyChanged(nameof(VisibleItems));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        foreach (TranscriptRowViewModel row in Items)
        {
            int at = row.TranscriptText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                row.Excerpt = "";
                continue;
            }

            int from = Math.Max(0, at - 60);
            int length = Math.Min(row.TranscriptText.Length - from, query.Length + 120);
            row.Excerpt = string.Concat(from > 0 ? "…" : "", row.TranscriptText.AsSpan(from, length), "…");
        }

        OnPropertyChanged(nameof(VisibleItems));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Rows currently visible — the unfiltered list, or only the hits once searching.</summary>
    public IEnumerable<TranscriptRowViewModel> VisibleItems =>
        _searchText.Trim().Length == 0
            ? Items
            : Items.Where(i => i.Excerpt.Length > 0);

    /// <summary>True when nothing is listed at all (no recordings, or no search hits).</summary>
    public bool IsEmpty => !VisibleItems.Any();

    private async Task DownloadModelAsync()
    {
        var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        DownloadProgress = 0;
        IsDownloadingModel = true;
        IsBusy = true;
        StatusText = Strings.Transcripts_Downloading;
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            await _models.DownloadAsync(SelectedModel, progress, cancellation.Token);
            StatusText = Strings.Transcripts_DownloadDone;
        }
        catch (OperationCanceledException)
        {
            StatusText = Strings.Transcripts_DownloadCancelled;
        }
        catch (Exception ex)
        {
            StatusText = "";
            _errors.Warn("transcript.download-failed", Strings.Transcripts_DownloadFailed, Strings.Transcripts_DownloadFailedHint);
            Serilog.Log.Warning(ex, "Whisper model download failed");
        }
        finally
        {
            _downloadCancellation = null;
            cancellation.Dispose();
            IsDownloadingModel = false;
            IsBusy = false;
            OnPropertyChanged(nameof(IsModelReady));
            OnPropertyChanged(nameof(ModelStatusText));
        }
    }

    private async Task TranscribeAsync(TranscriptRowViewModel? row)
    {
        if (row is null || IsBusy || row.IsTranscribing)
        {
            return;
        }

        if (!IsModelReady)
        {
            _errors.Warn("transcript.no-model", Strings.Transcripts_NeedModelFirst, Strings.Transcripts_NeedModelFirstHint);
            return;
        }

        row.IsTranscribing = true;
        IsBusy = true;
        StatusText = Strings.Transcripts_TranscribingNamed.Replace("{0}", row.DisplayName, StringComparison.Ordinal);
        try
        {
            var status = new Progress<string>(s => StatusText = $"{s} {row.DisplayName}");
            TranscriptDocument? document = await _transcription.TranscribeAsync(row.FilePath, SelectedModel, status);
            if (document is not null)
            {
                StatusText = document.Segments.Count == 0
                    ? Strings.Transcripts_NoSpeech
                    : Strings.Transcripts_Transcribed.Replace("{0}",
                        document.Segments.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal);
            }
            else
            {
                StatusText = "";
            }
        }
        finally
        {
            row.IsTranscribing = false;
            IsBusy = false;
            Reload();
        }
    }

    /// <summary>Writes the transcript to a file the user picks: plain text (the readable form), or the
    /// sidecar's own .srt/.vtt content when they ask for a caption file.</summary>
    private void SaveTranscript(TranscriptRowViewModel? row)
    {
        if (row is null || !row.HasTranscript)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = row.DisplayName,
            DefaultExt = ".txt",
            Filter = "Text file (*.txt)|*.txt|SubRip subtitles (*.srt)|*.srt|WebVTT captions (*.vtt)|*.vtt",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            string? sidecar = extension switch
            {
                ".srt" => ITranscriptionService.SrtPathFor(row.FilePath),
                ".vtt" => ITranscriptionService.VttPathFor(row.FilePath),
                _ => null,
            };

            if (sidecar is not null && File.Exists(sidecar))
            {
                File.Copy(sidecar, dialog.FileName, overwrite: true);
            }
            else
            {
                File.WriteAllText(dialog.FileName, row.TranscriptText);
            }

            StatusText = $"Saved {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Warn("transcript.save-failed", "Couldn't save the transcript there.", "Try another folder.");
        }
    }

    private void Open(TranscriptRowViewModel? row)    {
        if (row is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _errors.Warn("transcript.open-failed", Strings.Transcripts_OpenFailed, ex.Message);
        }
    }
}
