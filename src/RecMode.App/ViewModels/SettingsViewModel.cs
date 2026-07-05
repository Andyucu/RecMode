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
    private bool _startWithWindows;
    private bool _checkForUpdates;
    private int _cpuThreadCap;
    private bool _lowerEncoderPriority;
    private EncoderEffort _effort;

    public SettingsViewModel(ISettingsService settings, ThemeManager theme, IAppPaths paths, IStartupManager startup)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;
        _startup = startup;

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
        _checkForUpdates = s.CheckForUpdatesOnLaunch;
        _cpuThreadCap = s.CpuThreadCap;
        _lowerEncoderPriority = s.BelowNormalEncoderPriority;
        _effort = s.Effort;
        _startWithWindows = _startup.IsEnabled; // registry is the source of truth

        BrowseCommand = new RelayCommand(BrowseFolder);
    }

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    public IReadOnlyList<AccentColor> Accents { get; } =
        [AccentColor.Blue, AccentColor.Red, AccentColor.Purple, AccentColor.Teal, AccentColor.Orange];
    public IReadOnlyList<VideoCodec> Codecs { get; } = [VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1];
    public IReadOnlyList<MediaContainer> Containers { get; } =
        [MediaContainer.Mp4, MediaContainer.Mkv, MediaContainer.Mov, MediaContainer.WebM];
    public IReadOnlyList<AudioCodec> AudioCodecs { get; } = [AudioCodec.Aac, AudioCodec.Opus, AudioCodec.Flac];
    public IReadOnlyList<int> AudioBitrates { get; } = [128, 192, 256, 320];
    public IReadOnlyList<int> ThreadCaps { get; } = [0, 2, 4, 6, 8, 12, 16];
    public IReadOnlyList<EncoderEffort> Efforts { get; } =
        [EncoderEffort.Fast, EncoderEffort.Balanced, EncoderEffort.Quality];

    public ICommand BrowseCommand { get; }

    public string HotkeyStartStop => _settings.Current.HotkeyStartStop;
    public string HotkeyPauseResume => _settings.Current.HotkeyPauseResume;
    public string HotkeyScreenshot => _settings.Current.HotkeyScreenshot;

    public string VersionInfo
    {
        get
        {
            Version? v = Assembly.GetEntryAssembly()?.GetName().Version;
            string version = v is null ? "1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            return $"RecMode {version} · .NET 10 · WPF";
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
        set => Persist(ref _filenamePattern, value, v => _settings.Current.FilenamePattern = v);
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
