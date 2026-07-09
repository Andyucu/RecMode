using System.IO;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RecMode.App.Services;
using RecMode.App.Themes;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>
/// Full Settings screen (plan Phase 6, per the design): Appearance, Encoding defaults, Output, Recording,
/// Hotkeys (read-only until the Phase 9 remap UI), and General. Every change persists immediately via the
/// settings service; theme/accent apply live; "Start with Windows" writes the registry Run key.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _theme;
    private readonly IAppPaths _paths;
    private readonly IStartupManager _startup;
    private readonly Services.HotkeyBindings _hotkeys;
    private readonly Services.IUpdateChecker _updateChecker;
    private string? _capturingHotkey;
    private string _updateStatusText = "";
    private string? _updateReleasesUrl;
    private bool _canApplyUpdate;
    private bool _checkingForUpdates;

    private AppTheme _selectedTheme;
    private AccentColor _selectedAccent;
    private VideoCodec _selectedCodec;
    private MediaContainer _selectedContainer;
    private AudioCodec _selectedAudioCodec;
    private int _selectedAudioBitrate;
    private string _outputFolder;
    private string _filenamePattern;
    private bool _countdownEnabled;
    private bool _captureCursor;
    private bool _highlightClicks;
    private bool _autoSplitEnabled;
    private int _autoSplitSizeMb;
    private bool _startWithWindows;
    private bool _checkForUpdates;
    private int _cpuThreadCap;
    private bool _lowerEncoderPriority;
    private EncoderEffort _effort;
    private ShellLayout _layout;

    public SettingsViewModel(ISettingsService settings, ThemeManager theme, IAppPaths paths, IStartupManager startup,
        Services.HotkeyBindings hotkeys, Services.IUpdateChecker updateChecker)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;
        _startup = startup;
        _hotkeys = hotkeys;
        _updateChecker = updateChecker;

        RecModeSettings s = settings.Current;
        _selectedTheme = s.Theme;
        _selectedAccent = s.Accent;
        _selectedCodec = s.Codec;
        _selectedContainer = s.Container;
        _selectedAudioCodec = s.AudioCodec;
        _selectedAudioBitrate = s.AudioBitrateKbps;
        _outputFolder = s.OutputFolder ?? paths.RecordingsDirectory;
        _filenamePattern = s.FilenamePattern;
        _countdownEnabled = s.CountdownSeconds > 0;
        _captureCursor = s.CaptureCursor;
        _highlightClicks = s.HighlightClicks;
        _autoSplitEnabled = s.AutoSplitEnabled;
        _autoSplitSizeMb = AutoSplitSizes.Contains(s.AutoSplitSizeMb) ? s.AutoSplitSizeMb : 3900;
        _checkForUpdates = s.CheckForUpdatesOnLaunch;
        _cpuThreadCap = ThreadCaps.Contains(s.CpuThreadCap) ? s.CpuThreadCap : 0; // clamp a value from a bigger machine
        _lowerEncoderPriority = s.BelowNormalEncoderPriority;
        _effort = s.Effort;
        _layout = s.Layout;
        _startWithWindows = _startup.IsEnabled; // registry is the source of truth

        BrowseCommand = new RelayCommand(BrowseFolder);
        ChangeHotkeyCommand = new RelayCommand<string>(BeginCapture);
        CancelHotkeyCommand = new RelayCommand(CancelCapture);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !_checkingForUpdates);
        ApplyUpdateCommand = new AsyncRelayCommand(ApplyUpdateAsync, () => _canApplyUpdate);
    }

    public IReadOnlyList<ShellLayout> Layouts { get; } = [ShellLayout.Sidebar, ShellLayout.TopTab, ShellLayout.Compact];
    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    public IReadOnlyList<AccentColor> Accents { get; } =
        [AccentColor.Blue, AccentColor.Red, AccentColor.Purple, AccentColor.Teal, AccentColor.Orange];
    public IReadOnlyList<VideoCodec> Codecs { get; } = [VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1];
    public IReadOnlyList<MediaContainer> Containers { get; } =
        [MediaContainer.Mp4, MediaContainer.Mkv, MediaContainer.Mov, MediaContainer.WebM];
    public IReadOnlyList<AudioCodec> AudioCodecs { get; } = [AudioCodec.Aac, AudioCodec.Opus, AudioCodec.Flac];
    public IReadOnlyList<int> AudioBitrates { get; } = [128, 192, 256, 320];
    public IReadOnlyList<int> ThreadCaps { get; } = PerformanceBounds.ThreadCapOptions(Environment.ProcessorCount);
    public IReadOnlyList<int> AutoSplitSizes { get; } = [1024, 2048, 3900, 8000];
    public IReadOnlyList<EncoderEffort> Efforts { get; } =
        [EncoderEffort.Fast, EncoderEffort.Balanced, EncoderEffort.Quality];

    public ICommand BrowseCommand { get; }
    public IRelayCommand<string> ChangeHotkeyCommand { get; }
    public IRelayCommand CancelHotkeyCommand { get; }

    public string HotkeyStartStop => _settings.Current.HotkeyStartStop;
    public string HotkeyPauseResume => _settings.Current.HotkeyPauseResume;
    public string HotkeyScreenshot => _settings.Current.HotkeyScreenshot;
    public string HotkeyNextProfile => _settings.Current.HotkeyNextProfile;

    /// <summary>Non-null while listening for a new chord for one hotkey ("startstop" / "pause" / "screenshot").</summary>
    public bool IsCapturingHotkey => _capturingHotkey is not null;

    public string HotkeyCaptureHint => _capturingHotkey switch
    {
        "startstop" => "Press a shortcut for Start / stop…  (Esc to cancel)",
        "pause" => "Press a shortcut for Pause / resume…  (Esc to cancel)",
        "screenshot" => "Press a shortcut for Screenshot…  (Esc to cancel)",
        "nextprofile" => "Press a shortcut for Next profile…  (Esc to cancel)",
        _ => "",
    };

    private void BeginCapture(string? action)
    {
        _capturingHotkey = action;
        OnPropertyChanged(nameof(IsCapturingHotkey));
        OnPropertyChanged(nameof(HotkeyCaptureHint));
    }

    private void CancelCapture() => BeginCapture(null);

    /// <summary>Called by the view with the captured chord text; persists it and re-registers the global hotkeys.</summary>
    public void CompleteCapture(string chordText)
    {
        switch (_capturingHotkey)
        {
            case "startstop": _settings.Current.HotkeyStartStop = chordText; OnPropertyChanged(nameof(HotkeyStartStop)); break;
            case "pause": _settings.Current.HotkeyPauseResume = chordText; OnPropertyChanged(nameof(HotkeyPauseResume)); break;
            case "screenshot": _settings.Current.HotkeyScreenshot = chordText; OnPropertyChanged(nameof(HotkeyScreenshot)); break;
            case "nextprofile": _settings.Current.HotkeyNextProfile = chordText; OnPropertyChanged(nameof(HotkeyNextProfile)); break;
            default: return;
        }

        _settings.Save();       // write immediately so a crash can't lose a remap
        _hotkeys.Rebind();      // re-register the global hotkeys with the new chord
        CancelCapture();
    }

    public string VersionInfo
    {
        get
        {
            string? version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return $"RecMode {version ?? "0.9.0-beta"} · .NET 10 · WPF";
        }
    }

    // ---------- Updates (Phase 9/10 infrastructure — plan §3.5) ----------

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IAsyncRelayCommand ApplyUpdateCommand { get; }

    public string UpdateStatusText { get => _updateStatusText; private set => SetProperty(ref _updateStatusText, value); }
    public bool HasUpdateLink => _updateReleasesUrl is not null;
    public bool CanApplyUpdate => _canApplyUpdate;

    private async Task CheckForUpdatesAsync()
    {
        _checkingForUpdates = true;
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        UpdateStatusText = "Checking…";
        _updateReleasesUrl = null;
        _canApplyUpdate = false;

        Services.UpdateCheckResult result = await _updateChecker.CheckAsync();

        UpdateStatusText = result.Status switch
        {
            Services.UpdateCheckStatus.NotConfigured => "No update channel configured yet.",
            Services.UpdateCheckStatus.UpToDate => "You're up to date.",
            Services.UpdateCheckStatus.UpdateAvailable => $"Update available: v{result.Version}",
            Services.UpdateCheckStatus.Failed => $"Couldn't check for updates ({result.Error}).",
            _ => "",
        };
        _updateReleasesUrl = result.ReleasesPageUrl;
        _canApplyUpdate = result.CanApply;
        OnPropertyChanged(nameof(HasUpdateLink));
        OnPropertyChanged(nameof(CanApplyUpdate));
        ApplyUpdateCommand.NotifyCanExecuteChanged();

        _checkingForUpdates = false;
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyUpdateAsync()
    {
        UpdateStatusText = "Downloading update…";
        await _updateChecker.ApplyAndRestartAsync();
    }

    /// <summary>Opens the portable-mode "view release" link in the default browser.</summary>
    public void OpenUpdateLink()
    {
        if (_updateReleasesUrl is { } url)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    public ShellLayout SelectedLayout
    {
        get => _layout;
        set
        {
            if (SetProperty(ref _layout, value))
            {
                _settings.Current.Layout = value;
                _settings.Save(); // save now (not debounced) so the shell switches layout immediately
            }
        }
    }

    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _settings.Current.Theme = value;
                _theme.ApplyTheme(value);
                _theme.ApplyAccent(_settings.Current.Accent);
                _settings.RequestSave();
            }
        }
    }

    public AccentColor SelectedAccent
    {
        get => _selectedAccent;
        set
        {
            if (SetProperty(ref _selectedAccent, value))
            {
                _settings.Current.Accent = value;
                _theme.ApplyAccent(value);
                _settings.RequestSave();
            }
        }
    }

    public VideoCodec SelectedCodec
    {
        get => _selectedCodec;
        set => Persist(ref _selectedCodec, value, v => _settings.Current.Codec = v);
    }

    public MediaContainer SelectedContainer
    {
        get => _selectedContainer;
        set => Persist(ref _selectedContainer, value, v => _settings.Current.Container = v);
    }

    public AudioCodec SelectedAudioCodec
    {
        get => _selectedAudioCodec;
        set => Persist(ref _selectedAudioCodec, value, v => _settings.Current.AudioCodec = v);
    }

    public int SelectedAudioBitrate
    {
        get => _selectedAudioBitrate;
        set => Persist(ref _selectedAudioBitrate, value, v => _settings.Current.AudioBitrateKbps = v);
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set => Persist(ref _outputFolder, value, v => _settings.Current.OutputFolder = v);
    }

    public string FilenamePattern
    {
        get => _filenamePattern;
        set
        {
            Persist(ref _filenamePattern, value, v => _settings.Current.FilenamePattern = v);
            OnPropertyChanged(nameof(FilenamePatternPreview));
        }
    }

    /// <summary>Live example of the current pattern resolved against "now" — e.g. "RecMode {date} {time} → RecMode 2026-07-03 14-22-05.mp4".</summary>
    public string FilenamePatternPreview
    {
        get
        {
            string example = RecMode.Core.Recording.FilenameBuilder.BuildFileName(
                FilenamePattern, DateTimeOffset.Now, "Display", "H264", "mp4");
            return $"{FilenamePattern} → {example}";
        }
    }

    public bool CountdownEnabled
    {
        get => _countdownEnabled;
        set => Persist(ref _countdownEnabled, value, v => _settings.Current.CountdownSeconds = v ? 3 : 0);
    }

    public bool CaptureCursor
    {
        get => _captureCursor;
        set => Persist(ref _captureCursor, value, v => _settings.Current.CaptureCursor = v);
    }

    public bool HighlightClicks
    {
        get => _highlightClicks;
        set => Persist(ref _highlightClicks, value, v => _settings.Current.HighlightClicks = v);
    }

    public bool AutoSplitEnabled
    {
        get => _autoSplitEnabled;
        set => Persist(ref _autoSplitEnabled, value, v => _settings.Current.AutoSplitEnabled = v);
    }

    public int AutoSplitSizeMb
    {
        get => _autoSplitSizeMb;
        set => Persist(ref _autoSplitSizeMb, value, v => _settings.Current.AutoSplitSizeMb = v);
    }

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set => Persist(ref _checkForUpdates, value, v => _settings.Current.CheckForUpdatesOnLaunch = v);
    }

    public int CpuThreadCap
    {
        get => _cpuThreadCap;
        set => Persist(ref _cpuThreadCap, value, v => _settings.Current.CpuThreadCap = v);
    }

    public bool LowerEncoderPriority
    {
        get => _lowerEncoderPriority;
        set => Persist(ref _lowerEncoderPriority, value, v => _settings.Current.BelowNormalEncoderPriority = v);
    }

    public EncoderEffort SelectedEffort
    {
        get => _effort;
        set => Persist(ref _effort, value, v => _settings.Current.Effort = v);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                _startup.SetEnabled(value); // registry Run key (labelled opt-in exception, §3.5)
                _settings.Current.StartWithWindows = value;
                _settings.RequestSave();
            }
        }
    }

    private void Persist<T>(ref T field, T value, Action<T> apply)
    {
        if (SetProperty(ref field, value))
        {
            apply(value);
            _settings.RequestSave();
        }
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose output folder",
            InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : _paths.RecordingsDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            OutputFolder = dialog.FolderName;
        }
    }
}
