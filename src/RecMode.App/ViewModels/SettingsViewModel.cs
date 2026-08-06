using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RecMode.App.Services;
using RecMode.App.Themes;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Input;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>
/// Full Settings screen (plan Phase 6, per the design): Appearance, Encoding defaults, Output, Recording,
/// Hotkeys (read-only until the Phase 9 remap UI), and General. Every change persists immediately via the
/// settings service; theme/accent apply live; "Start with Windows" writes the registry Run key.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _theme;
    private readonly IAppPaths _paths;
    private readonly IStartupManager _startup;
    private readonly Services.HotkeyBindings _hotkeys;
    private readonly Services.IUpdateChecker _updateChecker;
    private readonly IErrorReporter _errors;
    private string? _capturingHotkey;
    private readonly DispatcherTimer _captureTimeoutTimer;
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
    private int _audioSyncOffsetMs;
    private bool _captureCommunicationsRoleAudio;
    private string _outputFolder;
    private string _filenamePattern;
    private bool _countdownEnabled;
    private bool _captureCursor;
    private bool _highlightClicks;
    private bool _showKeystrokes;
    private bool _autoZoomEnabled;
    private bool _autoSplitEnabled;
    private int _autoSplitSizeMb;
    private bool _startWithWindows;
    private bool _closeToTray;
    private bool _enableCrashMinidumps;
    private bool _checkForUpdates;
    private int _cpuThreadCap;
    private bool _lowerEncoderPriority;
    private bool _bitrateGuardrailEnabled;
    private EncoderEffort _effort;
    private ShellLayout _layout;

    public SettingsViewModel(ISettingsService settings, ThemeManager theme, IAppPaths paths, IStartupManager startup,
        Services.HotkeyBindings hotkeys, Services.IUpdateChecker updateChecker, IErrorReporter errors)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;
        _startup = startup;
        _hotkeys = hotkeys;
        _updateChecker = updateChecker;
        _errors = errors;

        RecModeSettings s = settings.Current;
        _selectedTheme = s.Theme;
        _selectedAccent = s.Accent;
        _selectedCodec = s.Codec;
        _selectedContainer = s.Container;
        _selectedAudioCodec = s.AudioCodec;
        _selectedAudioBitrate = s.AudioBitrateKbps;
        _audioSyncOffsetMs = Math.Clamp(s.AudioSyncOffsetMs, -500, 500);
        _captureCommunicationsRoleAudio = s.CaptureCommunicationsRoleAudio;
        _outputFolder = paths.ResolveUserPath(s.OutputFolder) ?? paths.RecordingsDirectory;
        _filenamePattern = s.FilenamePattern;
        _countdownEnabled = s.CountdownSeconds > 0;
        _captureCursor = s.CaptureCursor;
        _highlightClicks = s.HighlightClicks;
        _showKeystrokes = s.ShowKeystrokes;
        _autoZoomEnabled = s.AutoZoomEnabled;
        _autoSplitEnabled = s.AutoSplitEnabled;
        _autoSplitSizeMb = AutoSplitSizes.Contains(s.AutoSplitSizeMb) ? s.AutoSplitSizeMb : 3900;
        _checkForUpdates = s.CheckForUpdatesOnLaunch;
        _cpuThreadCap = ThreadCaps.Contains(s.CpuThreadCap) ? s.CpuThreadCap : 0; // clamp a value from a bigger machine
        _lowerEncoderPriority = s.BelowNormalEncoderPriority;
        _bitrateGuardrailEnabled = s.BitrateGuardrailEnabled;
        _effort = s.Effort;
        _layout = s.Layout;
        _startWithWindows = _startup.IsEnabled; // registry is the source of truth
        _closeToTray = s.CloseToTray;
        _enableCrashMinidumps = s.EnableCrashMinidumps;

        // Capturing a hotkey suspends every global hotkey for the duration (see HotkeyBindings.Suspend) —
        // if the user abandons the capture without going through CancelCapture/CompleteCapture (Alt-Tab
        // away and never comes back; or, worse, clicks the Compact layout radio on this same page, which
        // swaps the shell window via ShellPresenter without ever navigating away from Settings, so
        // OnNavigatedFrom never fires either), every hotkey stayed dead for the rest of the session with no
        // visible cue why. A timeout is the one recovery path that doesn't depend on guessing every possible
        // abandonment route — it fires regardless of how capture was left hanging.
        _captureTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _captureTimeoutTimer.Tick += (_, _) => CancelCapture();

        BrowseCommand = new RelayCommand(BrowseFolder);
        ChangeHotkeyCommand = new RelayCommand<string>(BeginCapture);
        CancelHotkeyCommand = new RelayCommand(CancelCapture);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !_checkingForUpdates);
        ApplyUpdateCommand = new AsyncRelayCommand(ApplyUpdateAsync, () => _canApplyUpdate);
    }

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
    public string HotkeyMicMute => _settings.Current.HotkeyMicMute;

    /// <summary>Non-null while listening for a new chord for one hotkey ("startstop" / "pause" / "screenshot").</summary>
    public bool IsCapturingHotkey => _capturingHotkey is not null;

    public string HotkeyCaptureHint => _capturingHotkey switch
    {
        "startstop" => "Press a shortcut for Start / stop…  (Esc to cancel)",
        "pause" => "Press a shortcut for Pause / resume…  (Esc to cancel)",
        "screenshot" => "Press a shortcut for Screenshot…  (Esc to cancel)",
        "nextprofile" => "Press a shortcut for Next profile…  (Esc to cancel)",
        "micmute" => "Press a shortcut for Mute mic…  (Esc to cancel)",
        _ => "",
    };

    private void BeginCapture(string? action)
    {
        bool wasCapturing = _capturingHotkey is not null;
        _capturingHotkey = action;
        OnPropertyChanged(nameof(IsCapturingHotkey));
        OnPropertyChanged(nameof(HotkeyCaptureHint));

        // Suspend/resume RecMode's own global hotkeys around the capture session — see
        // HotkeyBindings.Suspend's doc comment for why a bound chord would otherwise never reach this UI.
        // Guarded by wasCapturing so switching from capturing one action straight to another (clicking a
        // different row's "Change" button mid-capture) doesn't redundantly suspend/resume in between.
        if (action is not null && !wasCapturing)
        {
            _hotkeys.Suspend();
            _captureTimeoutTimer.Start();
        }
        else if (action is null && wasCapturing)
        {
            _hotkeys.Resume();
            _captureTimeoutTimer.Stop();
        }
    }

    private void CancelCapture() => BeginCapture(null);

    /// <summary>Called by the view with the captured chord text; persists it and re-registers the global hotkeys.</summary>
    public void CompleteCapture(string chordText)
    {
        if (!HotkeyChord.TryParse(chordText, out HotkeyChord captured))
        {
            _errors.Warn("hotkey.invalid", "That shortcut isn't valid.", "Use a key such as F9 or Ctrl+Shift+R.");
            return;
        }

        // A modifier-less chord is only sane for a function key (F1-F24, VK 0x70-0x87) — the one class of
        // key that isn't also normal typing input. Nothing here or in HotkeyChord.TryParse previously
        // rejected a bare letter/digit/Space/Enter/Tab/Backspace: capturing "S" for Screenshot, say (an
        // entirely plausible stray keypress inside the 15s capture window) registered and persisted, and from
        // then on pressing S in ANY application on the machine never reached the focused window — it silently
        // fired RecMode's screenshot instead. Space/Enter/Tab additionally broke the Settings UI's own
        // keyboard operation, including the very "Change" button flow used to fix the mistake.
        bool isFunctionKey = captured.VirtualKey is >= 0x70 and <= 0x87;
        if (captured.Modifiers == 0 && !isFunctionKey)
        {
            _errors.Warn("hotkey.invalid", "That shortcut needs a modifier.",
                "Add Ctrl, Alt, Shift, or Win (for example Ctrl+Shift+R) — or use a function key like F9 on its own.");
            return;
        }

        if (IsDuplicateHotkey(_capturingHotkey, captured))
        {
            _errors.Warn("hotkey.duplicate", "That shortcut is already assigned.", "Choose a different shortcut for each action.");
            return;
        }

        // Validate the specific chord the user is trying to set BEFORE committing it to settings — decoupled
        // from whether any of RecMode's *other* hotkeys can currently register. HotkeyBindings.Rebind()
        // re-registers all five independently and tolerates any one of them being taken by an unrelated app
        // (e.g. Teams holding Ctrl+Shift+M for its own mute toggle); without this pre-check that unrelated
        // collision used to fail the whole rebind and silently revert whatever the user actually just changed.
        if (!_hotkeys.CanRegister(captured))
        {
            _errors.Warn("hotkey.in-use", "That shortcut is already in use.", "Choose a different shortcut.");
            return;
        }

        switch (_capturingHotkey)
        {
            case "startstop": _settings.Current.HotkeyStartStop = chordText; OnPropertyChanged(nameof(HotkeyStartStop)); break;
            case "pause": _settings.Current.HotkeyPauseResume = chordText; OnPropertyChanged(nameof(HotkeyPauseResume)); break;
            case "screenshot": _settings.Current.HotkeyScreenshot = chordText; OnPropertyChanged(nameof(HotkeyScreenshot)); break;
            case "nextprofile": _settings.Current.HotkeyNextProfile = chordText; OnPropertyChanged(nameof(HotkeyNextProfile)); break;
            case "micmute": _settings.Current.HotkeyMicMute = chordText; OnPropertyChanged(nameof(HotkeyMicMute)); break;
            default: return;
        }

        _settings.Save();       // write immediately so a crash can't lose a remap
        CancelCapture();        // also resumes the (still-suspended-for-capture) global hotkeys from the new settings
    }

    private bool IsDuplicateHotkey(string? action, HotkeyChord captured)
    {
        IEnumerable<(string Action, string? Chord)> configured =
        [
            ("startstop", _settings.Current.HotkeyStartStop),
            ("pause", _settings.Current.HotkeyPauseResume),
            ("screenshot", _settings.Current.HotkeyScreenshot),
            ("nextprofile", _settings.Current.HotkeyNextProfile),
            ("micmute", _settings.Current.HotkeyMicMute),
        ];

        return configured.Any(item => item.Action != action &&
            HotkeyChord.TryParse(item.Chord, out HotkeyChord existing) && existing == captured);
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
        UpdateStatusText = Resources.Strings.Settings_UpdateChecking;
        _updateReleasesUrl = null;
        _canApplyUpdate = false;

        Services.UpdateCheckResult result;
        try
        {
            result = await _updateChecker.CheckAsync();
        }
        catch (Exception ex)
        {
            result = new Services.UpdateCheckResult { Status = Services.UpdateCheckStatus.Failed, Error = ex.Message };
        }

        UpdateStatusText = result.Status switch
        {
            Services.UpdateCheckStatus.NotConfigured => Resources.Strings.Settings_UpdateNotConfigured,
            Services.UpdateCheckStatus.UpToDate => Resources.Strings.Settings_UpdateUpToDate,
            Services.UpdateCheckStatus.UpdateAvailable => Resources.Strings.Settings_UpdateAvailable.Replace("{0}", result.Version ?? string.Empty, StringComparison.Ordinal),
            Services.UpdateCheckStatus.Failed => Resources.Strings.Settings_UpdateFailed,
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
        UpdateStatusText = Resources.Strings.Settings_UpdateDownloading;
        try
        {
            await _updateChecker.ApplyAndRestartAsync();
        }
        catch (Exception ex)
        {
            UpdateStatusText = "Couldn't install the update. Please try again.";
            _errors.Warn("update.apply-failed", "Couldn't install the update.", "Check your connection and install folder.", ex);
        }
    }

    /// <summary>Opens the portable-mode "view release" link in the default browser.</summary>
    public void OpenUpdateLink()
    {
        // _updateReleasesUrl is deserialized verbatim from the GitHub API response, and UseShellExecute=true
        // will happily launch a local path or any registered protocol handler, not just a browser. The
        // transport is TLS to a fixed repo, so this is defense-in-depth rather than a live hole — but it's the
        // one place in the app where a network-sourced string reaches a shell execute, so require http(s).
        if (_updateReleasesUrl is not { } url ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No default browser / no http association — a locked-down or stripped Windows image. Warn
            // rather than letting it reach the global handler's "unexpected error" crash modal.
            _errors.Warn("update.open-link-failed", "Couldn't open the download page.",
                "Copy the link from the RecMode releases page in your browser instead.", ex);
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

    /// <summary>A/V sync offset in ms (see <see cref="RecModeSettings.AudioSyncOffsetMs"/>). Manual and
    /// defaulted to 0 on purpose — RecMode's own measurements couldn't establish a compensation constant that
    /// would be right across capture paths and hardware, and every comparable recorder (OBS included) ships
    /// this as a manual control too.</summary>
    public int AudioSyncOffsetMs
    {
        get => _audioSyncOffsetMs;
        set
        {
            Persist(ref _audioSyncOffsetMs, value, v => _settings.Current.AudioSyncOffsetMs = v);
            // Raised unconditionally rather than only on change: Persist() doesn't report whether it took,
            // and both of these are derived read-only projections, so a redundant notification is free.
            OnPropertyChanged(nameof(AudioSyncOffsetLabelText));
            OnPropertyChanged(nameof(AudioSyncOffsetDescription));
        }
    }

    /// <summary>Signed, unit-suffixed readout next to the slider (e.g. "+120 ms").</summary>
    public string AudioSyncOffsetLabelText =>
        _audioSyncOffsetMs == 0 ? "0 ms" : $"{_audioSyncOffsetMs:+#;-#;0} ms";

    /// <summary>Explains which way the current value shifts things, in the user's own terms — "the sound runs
    /// ahead" / "the sound lags" is what someone actually notices, whereas a bare signed millisecond value
    /// gives no clue which direction to drag the slider. Deliberately carries no number: the exact value is
    /// already displayed by <see cref="AudioSyncOffsetLabelText"/> right beside the slider, so repeating it
    /// here would be duplicate (and separately-localized) UI text saying the same thing twice.</summary>
    public string AudioSyncOffsetDescription => _audioSyncOffsetMs switch
    {
        0 => Resources.Strings.Settings_AudioSyncOffsetInSync,
        > 0 => Resources.Strings.Settings_AudioSyncOffsetDelayed,
        _ => Resources.Strings.Settings_AudioSyncOffsetAdvanced,
    };

    /// <summary>Displayed and picked as an absolute path, but <em>persisted</em> via
    /// <see cref="IAppPaths.ToPortableSetting"/> — relative when it lives inside the app folder, so moving a
    /// portable install doesn't leave it pointing at the old machine's absolute path. See that method for why.</summary>
    public string OutputFolder
    {
        get => _outputFolder;
        set => Persist(ref _outputFolder, value, v => _settings.Current.OutputFolder = _paths.ToPortableSetting(v));
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

    public bool ShowKeystrokes
    {
        get => _showKeystrokes;
        set => Persist(ref _showKeystrokes, value, v => _settings.Current.ShowKeystrokes = v);
    }

    public bool AutoZoomEnabled
    {
        get => _autoZoomEnabled;
        set => Persist(ref _autoZoomEnabled, value, v => _settings.Current.AutoZoomEnabled = v);
    }

    /// <summary>Gates <c>AudioMixer.StartCommsLoopbackIfDifferent</c> — see
    /// <see cref="RecMode.Core.Settings.RecModeSettings.CaptureCommunicationsRoleAudio"/>'s own doc comment
    /// for why this defaults on (catching Teams/Zoom audio) despite a real echo risk on some setups; this is
    /// the toggle a user hits if they need the opt-out.</summary>
    public bool CaptureCommunicationsRoleAudio
    {
        get => _captureCommunicationsRoleAudio;
        set => Persist(ref _captureCommunicationsRoleAudio, value, v => _settings.Current.CaptureCommunicationsRoleAudio = v);
    }

    /// <summary>Adds a generous -maxrate/-bufsize ceiling alongside CRF/CQ encoding on encoders whose
    /// rate-control mode supports it, to guard against surprise multi-GB files on unusually complex content.</summary>
    public bool BitrateGuardrailEnabled
    {
        get => _bitrateGuardrailEnabled;
        set => Persist(ref _bitrateGuardrailEnabled, value, v => _settings.Current.BitrateGuardrailEnabled = v);
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

    /// <summary>When enabled, the caption-bar close (×) button hides the window to the tray instead of
    /// quitting — the same fate minimize already gets unconditionally (<see cref="TrayIconService"/>). The
    /// tray menu's own "Quit" always exits regardless of this setting, since it doesn't go through the
    /// window's Close() at all.</summary>
    public bool CloseToTray
    {
        get => _closeToTray;
        set => Persist(ref _closeToTray, value, v => _settings.Current.CloseToTray = v);
    }

    /// <summary>Opt-in local crash minidumps (§3.6) — off by default. The setting and the writer
    /// (<see cref="ICrashReporter"/>/<see cref="IMinidumpWriter"/>) have existed since early in the project,
    /// but nothing in the UI ever exposed a way to turn this on; it could only be enabled by hand-editing
    /// settings.json. Dumps are local-only and never uploaded (see <see cref="MinidumpWriter"/> — no
    /// telemetry, ever), and only capture module data/handles/thread stacks, not the managed heap.</summary>
    public bool EnableCrashMinidumps
    {
        get => _enableCrashMinidumps;
        set => Persist(ref _enableCrashMinidumps, value, v => _settings.Current.EnableCrashMinidumps = v);
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
            Title = Resources.Strings.Settings_ChooseOutputFolder,
            InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : _paths.RecordingsDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            OutputFolder = dialog.FolderName;
        }
    }

    public void OnNavigatedTo() => RefreshFromSettings();

    /// <summary>Cancels any in-progress hotkey capture on leaving the page — otherwise navigating away
    /// mid-capture (e.g. clicking Library) would leave every global hotkey suspended (see
    /// <see cref="HotkeyBindings.Suspend"/>) for the rest of the session, since nothing else would ever
    /// call <see cref="CancelCapture"/> to resume them.</summary>
    public void OnNavigatedFrom() => CancelCapture();

    private void RefreshFromSettings()
    {
        RecModeSettings s = _settings.Current;
        _selectedTheme = s.Theme;
        _selectedAccent = s.Accent;
        _selectedCodec = s.Codec;
        _selectedContainer = s.Container;
        _selectedAudioCodec = s.AudioCodec;
        _selectedAudioBitrate = s.AudioBitrateKbps;
        _audioSyncOffsetMs = Math.Clamp(s.AudioSyncOffsetMs, -500, 500);
        _captureCommunicationsRoleAudio = s.CaptureCommunicationsRoleAudio;
        _outputFolder = _paths.ResolveUserPath(s.OutputFolder) ?? _paths.RecordingsDirectory;
        _filenamePattern = s.FilenamePattern;
        _countdownEnabled = s.CountdownSeconds > 0;
        _captureCursor = s.CaptureCursor;
        _highlightClicks = s.HighlightClicks;
        _showKeystrokes = s.ShowKeystrokes;
        _autoZoomEnabled = s.AutoZoomEnabled;
        _autoSplitEnabled = s.AutoSplitEnabled;
        _autoSplitSizeMb = AutoSplitSizes.Contains(s.AutoSplitSizeMb) ? s.AutoSplitSizeMb : 3900;
        _checkForUpdates = s.CheckForUpdatesOnLaunch;
        _cpuThreadCap = ThreadCaps.Contains(s.CpuThreadCap) ? s.CpuThreadCap : 0;
        _lowerEncoderPriority = s.BelowNormalEncoderPriority;
        _bitrateGuardrailEnabled = s.BitrateGuardrailEnabled;
        _effort = s.Effort;
        _layout = s.Layout;
        _startWithWindows = _startup.IsEnabled;
        _closeToTray = s.CloseToTray;
        _enableCrashMinidumps = s.EnableCrashMinidumps;
        foreach (string property in new[] { nameof(SelectedTheme), nameof(SelectedAccent), nameof(SelectedCodec),
            nameof(SelectedContainer), nameof(SelectedAudioCodec), nameof(SelectedAudioBitrate),
            nameof(AudioSyncOffsetMs), nameof(AudioSyncOffsetLabelText), nameof(AudioSyncOffsetDescription), nameof(CaptureCommunicationsRoleAudio), nameof(OutputFolder),
            nameof(FilenamePattern), nameof(FilenamePatternPreview), nameof(CountdownEnabled), nameof(CaptureCursor),
            nameof(HighlightClicks), nameof(ShowKeystrokes), nameof(AutoZoomEnabled), nameof(AutoSplitEnabled), nameof(AutoSplitSizeMb), nameof(CheckForUpdates),
            nameof(CpuThreadCap), nameof(LowerEncoderPriority), nameof(BitrateGuardrailEnabled), nameof(SelectedEffort),
            nameof(SelectedLayout), nameof(StartWithWindows), nameof(CloseToTray), nameof(EnableCrashMinidumps), nameof(HotkeyStartStop), nameof(HotkeyPauseResume),
            nameof(HotkeyScreenshot), nameof(HotkeyNextProfile), nameof(HotkeyMicMute) }) OnPropertyChanged(property);
    }
}
