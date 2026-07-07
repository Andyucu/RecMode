using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Capture.Webcam;
using RecMode.Core.Recording;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.App.ViewModels;

/// <summary>
/// The Record screen. Phase 1 gave it the functional record path; Phase 2 adds a live preview (WGC → scaled
/// BGRA → WriteableBitmap, ≤ 30 fps, torn down on nav-away/minimize/record per §3.9) and a window source.
/// </summary>
public sealed class RecordViewModel : ObservableObject, INavigationAware
{
    private readonly RecordingCoordinator _coordinator;
    private readonly IEncoderProbe _encoderProbe;
    private readonly ISettingsService _settings;
    private readonly Func<IPreviewEngine> _previewFactory;
    private readonly IRegionPicker _regionPicker;
    private readonly Func<RecMode.Audio.IAudioMixer> _mixerFactory;
    private RecMode.Audio.IAudioMixer? _meterMixer;
    private System.Windows.Threading.DispatcherTimer? _meterTimer;

    private MonitorInfo? _selectedMonitor;
    private WindowInfo? _selectedWindow;
    private EncoderInfo? _selectedEncoder;
    private MediaContainer _selectedFormat;
    private int _selectedFrameRate;
    private int _quality;
    private RegionRect? _region;
    private bool _isScreenSource = true;
    private bool _isWindowSource;
    private bool _isRegionSource;
    private bool _selectingRegion;
    private bool _isRecording;
    private bool _isActivePage;
    private string _statusText = "Ready";
    private string _elapsedText = "00:00";
    private string _statsText = "";
    private bool _devicesLoaded;

    private IPreviewEngine? _preview;
    private WriteableBitmap? _previewBitmap;
    private byte[] _previewBuffer = [];
    private ImageSource? _previewImage;

    private readonly ScreenshotService _screenshots;
    private readonly IScreenshotFlash _screenshotFlash;
    private readonly ICountdownController _countdown;
    private readonly IProfileNamePrompt _profilePrompt;
    private readonly RecMode.Core.Infrastructure.IAppPaths _paths;
    private readonly RecordingProfile _customSentinel = new() { Name = Resources.Strings.Profile_Custom, IsBuiltIn = true };
    private RecordingProfile? _selectedProfile;
    private bool _loadingProfiles;
    private string _diskSpaceText = "";

    public RecordViewModel(RecordingCoordinator coordinator, IEncoderProbe encoderProbe,
        ISettingsService settings, Func<IPreviewEngine> previewFactory, IRegionPicker regionPicker,
        Func<RecMode.Audio.IAudioMixer> mixerFactory, ScreenshotService screenshots, IScreenshotFlash screenshotFlash,
        ICountdownController countdown, IProfileNamePrompt profilePrompt, RecMode.Core.Infrastructure.IAppPaths paths)
    {
        _coordinator = coordinator;
        _encoderProbe = encoderProbe;
        _settings = settings;
        _previewFactory = previewFactory;
        _regionPicker = regionPicker;
        _mixerFactory = mixerFactory;
        _screenshots = screenshots;
        _screenshotFlash = screenshotFlash;
        _countdown = countdown;
        _profilePrompt = profilePrompt;
        _paths = paths;
        _systemAudioEnabled = settings.Current.SystemAudioEnabled;
        _micEnabled = settings.Current.MicrophoneEnabled;
        _systemVolume = settings.Current.SystemVolume;
        _micVolume = settings.Current.MicVolume;
        _webcamEnabled = settings.Current.WebcamEnabled;
        _webcamPosition = settings.Current.WebcamPosition;
        _webcamSizePercent = settings.Current.WebcamSizePercent;

        if (settings.Current.RegionWidth > 0 && settings.Current.RegionHeight > 0)
        {
            _region = new RegionRect(settings.Current.RegionX, settings.Current.RegionY,
                settings.Current.RegionWidth, settings.Current.RegionHeight);
        }

        Formats = [MediaContainer.Mp4, MediaContainer.Mkv, MediaContainer.Mov, MediaContainer.WebM];
        FrameRates = [15, 30, 60, 120];
        _selectedFormat = Formats.Contains(settings.Current.Container) ? settings.Current.Container : MediaContainer.Mp4;
        _selectedFrameRate = FrameRates.Contains(settings.Current.FrameRate) ? settings.Current.FrameRate : 60;
        _quality = Math.Clamp(settings.Current.Quality, 0, 100);

        RecordCommand = new RelayCommand(ToggleRecord, () => CurrentTarget() is not null && SelectedEncoder is not null);
        ChangeRegionCommand = new RelayCommand(() => PickRegion(revertOnCancel: false));
        PauseResumeCommand = new RelayCommand(TogglePause);
        ScreenshotCommand = new RelayCommand(TakeScreenshot, () => CurrentTarget() is not null);
        ToggleAnnotateCommand = new RelayCommand(() => { if (_coordinator.IsRecording) IsAnnotating = !IsAnnotating; });
        SaveProfileCommand = new RelayCommand(SaveProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => CanDeleteProfile);

        LoadProfiles();

        _coordinator.ProgressChanged += OnProgress;
        _coordinator.Finished += OnFinished;
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public ObservableCollection<WindowInfo> Windows { get; } = [];
    public ObservableCollection<EncoderInfo> Encoders { get; } = [];
    public ObservableCollection<RecordingProfile> Profiles { get; } = [];
    public IReadOnlyList<MediaContainer> Formats { get; }
    public IReadOnlyList<int> FrameRates { get; }

    public IRelayCommand RecordCommand { get; }
    public IRelayCommand ChangeRegionCommand { get; }
    public IRelayCommand PauseResumeCommand { get; }
    public IRelayCommand ScreenshotCommand { get; }
    public IRelayCommand ToggleAnnotateCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand DeleteProfileCommand { get; }

    /// <summary>Captures a still of the current source (F11 / button). Runs on the UI thread.</summary>
    public void TakeScreenshot()
    {
        CaptureTarget? target = CurrentTarget();
        if (target is not null)
        {
            _screenshots.Capture(target);
            if (SelectedMonitor is { } mon)
            {
                _screenshotFlash.Flash(mon);
            }
        }
    }

    public ImageSource? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }
    public bool HasPreview => PreviewImage is not null;

    public bool IsScreenSource
    {
        get => _isScreenSource;
        set
        {
            if (SetProperty(ref _isScreenSource, value) && value)
            {
                OnPropertyChanged(nameof(ShowWindowPicker));
                RestartPreview();
                RecordCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsWindowSource
    {
        get => _isWindowSource;
        set
        {
            if (SetProperty(ref _isWindowSource, value) && value)
            {
                LoadWindows();
                OnPropertyChanged(nameof(ShowWindowPicker));
                RestartPreview();
                RecordCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRegionSource
    {
        get => _isRegionSource;
        set
        {
            if (!SetProperty(ref _isRegionSource, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowRegionInfo));
            RecordCommand.NotifyCanExecuteChanged();
            if (!value || _selectingRegion)
            {
                return;
            }

            // While actively recording, just reuse whatever region is already set — never pop the
            // full-screen picker over an in-progress capture.
            if (IsRecording)
            {
                RestartPreview();
                return;
            }

            // Re-prompt every time the Region tile is pressed (not just the first time ever). If a region
            // is already stored, cancelling keeps it (nothing forces a fallback); otherwise cancelling
            // reverts to Screen since there's no prior region to fall back to.
            PickRegion(revertOnCancel: _region is null);
        }
    }

    private void PickRegion(bool revertOnCancel)
    {
        if (SelectedMonitor is not { } mon)
        {
            if (revertOnCancel) RevertToScreen();
            return;
        }

        _selectingRegion = true;
        RegionRect? picked = _regionPicker.Pick(mon);
        _selectingRegion = false;

        if (picked is { } r)
        {
            _region = r;
            _settings.Current.RegionX = r.X;
            _settings.Current.RegionY = r.Y;
            _settings.Current.RegionWidth = r.Width;
            _settings.Current.RegionHeight = r.Height;
            _settings.RequestSave();
            OnPropertyChanged(nameof(RegionLabel));
            RestartPreview();
            RecordCommand.NotifyCanExecuteChanged();
        }
        else if (revertOnCancel)
        {
            RevertToScreen();
        }
        else
        {
            // Cancelled with an existing region to fall back to — (re-)apply it so the preview reflects
            // Region source even if this pick was triggered by switching tiles rather than "Change…".
            RestartPreview();
        }
    }

    public string RegionLabel => _region is { } r ? $"Region {r.Width} × {r.Height}" : "No region selected";
    public bool ShowWindowPicker => IsWindowSource;
    public bool ShowRegionInfo => IsRegionSource;

    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set { if (SetProperty(ref _selectedMonitor, value)) { RestartPreview(); RecordCommand.NotifyCanExecuteChanged(); } }
    }

    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set { if (SetProperty(ref _selectedWindow, value)) { RestartPreview(); RecordCommand.NotifyCanExecuteChanged(); } }
    }

    public EncoderInfo? SelectedEncoder
    {
        get => _selectedEncoder;
        set
        {
            if (SetProperty(ref _selectedEncoder, value) && value is not null)
            {
                _settings.Current.Codec = value.Codec;
                _settings.Current.Backend = value.Backend;
                _settings.Current.HardwareEncoding = value.IsHardware;
                _settings.RequestSave();
                OnPropertyChanged(nameof(HardwareBadge));
                RecordCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public MediaContainer SelectedFormat
    {
        get => _selectedFormat;
        set { if (SetProperty(ref _selectedFormat, value)) { _settings.Current.Container = value; _settings.RequestSave(); } }
    }

    public int SelectedFrameRate
    {
        get => _selectedFrameRate;
        set { if (SetProperty(ref _selectedFrameRate, value)) { _settings.Current.FrameRate = value; _settings.RequestSave(); } }
    }

    public int Quality
    {
        get => _quality;
        set
        {
            if (SetProperty(ref _quality, value))
            {
                _settings.Current.Quality = value;
                _settings.RequestSave();
                OnPropertyChanged(nameof(QualityLabel));
            }
        }
    }

    public string QualityLabel => $"{Quality} · CRF {FfmpegArgsBuilder.QualityToCrf(Quality)}";
    public string HardwareBadge => SelectedEncoder?.HardwareBadge ?? "";

    /// <summary>
    /// Recording profiles (plan §7 backlog #4, pulled forward): built-in presets (Tutorial/Gameplay/Meeting/
    /// Bug report/GIF clip/High-quality archive) plus any user-saved ones, plus a "Custom" sentinel meaning
    /// "no preset — settings below are edited directly". Picking one applies container/frame rate/quality/audio;
    /// it doesn't touch the source or the encoder (hw availability is machine-specific).
    /// </summary>
    public RecordingProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            // Profiles.Clear() (in LoadProfiles, e.g. after Save/Delete) momentarily nulls the ComboBox's
            // SelectedItem; that flows back through this TwoWay-bound setter and would otherwise clobber the
            // selection we're about to restore. Ignore side effects while a profile-list refresh is in flight.
            if (_loadingProfiles)
            {
                return;
            }

            _settings.Current.SelectedProfileName = ReferenceEquals(value, _customSentinel) ? null : value?.Name;
            _settings.RequestSave();
            OnPropertyChanged(nameof(CanDeleteProfile));
            DeleteProfileCommand.NotifyCanExecuteChanged();
            if (value is not null && !ReferenceEquals(value, _customSentinel))
            {
                ApplyProfile(value);
            }
        }
    }

    public bool CanDeleteProfile => SelectedProfile is { IsBuiltIn: false };

    private void LoadProfiles()
    {
        _loadingProfiles = true;
        try
        {
            Profiles.Clear();
            Profiles.Add(_customSentinel);
            foreach (RecordingProfile p in RecordingProfiles.BuiltIn)
            {
                Profiles.Add(p);
            }
            foreach (RecordingProfile p in _settings.Current.CustomProfiles)
            {
                Profiles.Add(p);
            }

            string? savedName = _settings.Current.SelectedProfileName;
            _selectedProfile = savedName is null ? _customSentinel : Profiles.FirstOrDefault(p => p.Name == savedName) ?? _customSentinel;
            OnPropertyChanged(nameof(SelectedProfile));
        }
        finally
        {
            _loadingProfiles = false;
        }

        OnPropertyChanged(nameof(CanDeleteProfile));
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    private void ApplyProfile(RecordingProfile profile)
    {
        SelectedFormat = profile.Container;
        if (FrameRates.Contains(profile.FrameRate))
        {
            SelectedFrameRate = profile.FrameRate;
        }
        Quality = profile.Quality;
        SystemAudioEnabled = profile.SystemAudioEnabled;
        MicEnabled = profile.MicrophoneEnabled;
        _settings.Current.AudioCodec = profile.AudioCodec;
        _settings.Current.AudioBitrateKbps = profile.AudioBitrateKbps;
        _settings.RequestSave();
    }

    private void SaveProfile()
    {
        string defaultName = SelectedProfile is { IsBuiltIn: false } current ? current.Name : "My profile";
        string? name = _profilePrompt.Prompt(defaultName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (RecordingProfiles.BuiltIn.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(Resources.Strings.Profile_NameTaken, Resources.Strings.Profile_SaveTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new RecordingProfile
        {
            Name = name,
            Container = SelectedFormat,
            FrameRate = SelectedFrameRate,
            Quality = Quality,
            SystemAudioEnabled = SystemAudioEnabled,
            MicrophoneEnabled = MicEnabled,
            AudioCodec = _settings.Current.AudioCodec,
            AudioBitrateKbps = _settings.Current.AudioBitrateKbps,
            IsBuiltIn = false,
        };

        _settings.Current.CustomProfiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _settings.Current.CustomProfiles.Add(profile);
        _settings.Current.SelectedProfileName = name;
        _settings.Save();

        LoadProfiles();
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is not { IsBuiltIn: false } profile)
        {
            return;
        }

        _settings.Current.CustomProfiles.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        _settings.Current.SelectedProfileName = null;
        _settings.Save();

        LoadProfiles();
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(RecordButtonText));
                OnPropertyChanged(nameof(CanEditSettings));
            }
        }
    }

    public bool CanEditSettings => !IsRecording;
    public string RecordButtonText => IsRecording ? "Stop" : "Record";

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        private set { if (SetProperty(ref _isPaused, value)) OnPropertyChanged(nameof(PauseButtonText)); }
    }

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    private bool _isHealthy = true;
    /// <summary>False when the encoder can't keep up (recording health, §3.6) — drives the toolbar badge.</summary>
    public bool IsHealthy { get => _isHealthy; private set => SetProperty(ref _isHealthy, value); }

    private bool _isAnnotating;
    /// <summary>True while draw-on-screen annotation is active (only meaningful during a recording, Phase 8).</summary>
    public bool IsAnnotating { get => _isAnnotating; private set => SetProperty(ref _isAnnotating, value); }

    /// <summary>Turns annotation off (called by the overlay on Esc and when the recording ends).</summary>
    public void StopAnnotating() => IsAnnotating = false;

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }
    public string StatsText { get => _statsText; private set => SetProperty(ref _statsText, value); }

    /// <summary>How much room is left on the output drive — "{used} of {total}" while recording, "{free} free of {total}" at rest.</summary>
    public string DiskSpaceText { get => _diskSpaceText; private set => SetProperty(ref _diskSpaceText, value); }

    /// <summary>Refreshes <see cref="DiskSpaceText"/> against the output folder's drive. Best-effort — a bad path or unready drive just clears the text.</summary>
    private void UpdateDiskSpaceText(long recordingBytes = 0)
    {
        try
        {
            string outputDir = _settings.Current.OutputFolder ?? _paths.RecordingsDirectory;
            string? root = Path.GetPathRoot(Path.GetFullPath(outputDir));
            if (root is null)
            {
                DiskSpaceText = "";
                return;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                DiskSpaceText = "";
                return;
            }

            string total = FormatBytes(drive.TotalSize);
            DiskSpaceText = recordingBytes > 0
                ? $"{FormatBytes(recordingBytes)} of {total}"
                : $"{FormatBytes(drive.AvailableFreeSpace)} free of {total}";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            DiskSpaceText = "";
        }
    }

    public void OnNavigatedTo()
    {
        _isActivePage = true;
        LoadDevices();
        LoadPerAppAudioTargets(); // refreshed every visit — the running-app list changes more than monitors/encoders
        LoadWebcamDevices();
        StartPreview();
        StartMetering();
        if (!IsRecording)
        {
            UpdateDiskSpaceText();
        }
    }

    /// <summary>
    /// Ensures a default source + encoder are selected without starting preview/metering. Lets the CLI
    /// (<c>--record</c>/<c>--screenshot</c>) act headlessly (e.g. <c>--tray</c>) before the view is shown.
    /// </summary>
    public void EnsureDevicesLoaded() => LoadDevices();

    public void OnNavigatedFrom()
    {
        _isActivePage = false;
        StopPreview();
        StopMetering();
    }

    /// <summary>Called by the shell on minimize/restore to honour the §3.9 "nothing runs when hidden" rule.</summary>
    public void SetWindowMinimized(bool minimized)
    {
        if (minimized)
        {
            StopPreview();
            StopMetering();
        }
        else if (_isActivePage)
        {
            if (!IsRecording)
            {
                StartPreview();
            }
            StartMetering();
        }
    }

    private CaptureTarget? CurrentTarget()
    {
        if (IsRegionSource)
        {
            return _region is { } r && SelectedMonitor is { } mon
                ? CaptureTarget.FromRegion(mon, r)
                : null;
        }

        if (IsWindowSource)
        {
            return SelectedWindow is null ? null : CaptureTarget.FromWindow(SelectedWindow);
        }

        return SelectedMonitor is null ? null : CaptureTarget.FromMonitor(SelectedMonitor);
    }

    private void RevertToScreen()
    {
        _isRegionSource = false;
        OnPropertyChanged(nameof(IsRegionSource));
        OnPropertyChanged(nameof(ShowRegionInfo));
        IsScreenSource = true; // re-checks the Screen tile and restarts preview
    }

    private void LoadDevices()
    {
        if (_devicesLoaded)
        {
            return;
        }

        Monitors.Clear();
        foreach (MonitorInfo m in CaptureCapabilities.EnumerateMonitors())
        {
            Monitors.Add(m);
        }
        SelectedMonitor = Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();

        Encoders.Clear();
        foreach (EncoderInfo e in _encoderProbe.GetAvailableEncoders())
        {
            Encoders.Add(e);
        }
        SelectedEncoder = PickDefaultEncoder();
        _devicesLoaded = true;
    }

    private void LoadWindows()
    {
        Windows.Clear();
        foreach (WindowInfo w in CaptureCapabilities.EnumerateWindows())
        {
            Windows.Add(w);
        }
        SelectedWindow ??= Windows.FirstOrDefault();
    }

    private EncoderInfo? PickDefaultEncoder()
    {
        EncoderInfo? saved = Encoders.FirstOrDefault(e =>
            e.Codec == _settings.Current.Codec && e.Backend == _settings.Current.Backend);
        return saved
            ?? Encoders.FirstOrDefault(e => e is { Codec: VideoCodec.H264, IsHardware: true })
            ?? Encoders.FirstOrDefault(e => e.Codec == VideoCodec.H264)
            ?? Encoders.FirstOrDefault();
    }

    // ---------- Audio mixer ----------

    private bool _systemAudioEnabled;
    private bool _micEnabled;
    private double _systemMeter;
    private double _micMeter;
    private double _systemVolume;
    private double _micVolume;

    // Per-app audio (plan §7): narrows "System audio" to one running app instead of the whole system.
    private readonly AudioProcessTarget _allAppsSentinel = new() { ProcessId = 0, ProcessName = "", WindowTitle = "All apps" };
    private AudioProcessTarget? _selectedPerAppAudioTarget;
    private bool _loadingPerAppTargets;

    public ObservableCollection<AudioProcessTarget> PerAppAudioTargets { get; } = [];

    public AudioProcessTarget? SelectedPerAppAudioTarget
    {
        get => _selectedPerAppAudioTarget;
        set
        {
            if (!SetProperty(ref _selectedPerAppAudioTarget, value) || _loadingPerAppTargets)
            {
                return;
            }

            _settings.Current.PerAppAudioProcessName = ReferenceEquals(value, _allAppsSentinel) ? null : value?.ProcessName;
            _settings.RequestSave();
            RestartMetering();
        }
    }

    private void LoadPerAppAudioTargets()
    {
        _loadingPerAppTargets = true;
        try
        {
            PerAppAudioTargets.Clear();
            PerAppAudioTargets.Add(_allAppsSentinel);
            foreach (AudioProcessTarget t in CaptureCapabilities.EnumerateAudioProcesses())
            {
                PerAppAudioTargets.Add(t);
            }

            string? savedName = _settings.Current.PerAppAudioProcessName;
            _selectedPerAppAudioTarget = string.IsNullOrEmpty(savedName)
                ? _allAppsSentinel
                : PerAppAudioTargets.FirstOrDefault(t => string.Equals(t.ProcessName, savedName, StringComparison.OrdinalIgnoreCase)) ?? _allAppsSentinel;
            OnPropertyChanged(nameof(SelectedPerAppAudioTarget));
        }
        finally
        {
            _loadingPerAppTargets = false;
        }
    }

    /// <summary>
    /// The PID to narrow system-audio capture to, or null for the whole system. Always null for now — the
    /// "Limit to app" control is hidden (see RecordView.xaml) because process-loopback isolation doesn't
    /// actually hold, so metering must reflect the same full-system behavior a real recording will use.
    /// </summary>
    private int? PerAppAudioTargetPid => null;

    public bool SystemAudioEnabled
    {
        get => _systemAudioEnabled;
        set
        {
            if (SetProperty(ref _systemAudioEnabled, value))
            {
                _settings.Current.SystemAudioEnabled = value;
                _settings.RequestSave();
                RestartMetering();
            }
        }
    }

    public bool MicEnabled
    {
        get => _micEnabled;
        set
        {
            if (SetProperty(ref _micEnabled, value))
            {
                _settings.Current.MicrophoneEnabled = value;
                _settings.RequestSave();
                RestartMetering();
            }
        }
    }

    /// <summary>RMS level 0..1 for the meter bars.</summary>
    public double SystemMeter { get => _systemMeter; private set => SetProperty(ref _systemMeter, value); }
    public double MicMeter { get => _micMeter; private set => SetProperty(ref _micMeter, value); }

    /// <summary>Per-source capture volume 0..100 (→ mixer gain). Applies live to metering and the active recording.</summary>
    public double SystemVolume
    {
        get => _systemVolume;
        set
        {
            if (SetProperty(ref _systemVolume, value))
            {
                _settings.Current.SystemVolume = (int)Math.Round(value);
                _settings.RequestSave();
                OnPropertyChanged(nameof(SystemVolumeLabel));
                ApplyGains();
            }
        }
    }

    public double MicVolume
    {
        get => _micVolume;
        set
        {
            if (SetProperty(ref _micVolume, value))
            {
                _settings.Current.MicVolume = (int)Math.Round(value);
                _settings.RequestSave();
                OnPropertyChanged(nameof(MicVolumeLabel));
                ApplyGains();
            }
        }
    }

    public string SystemVolumeLabel => $"{(int)Math.Round(SystemVolume)}%";
    public string MicVolumeLabel => $"{(int)Math.Round(MicVolume)}%";

    private void ApplyGains()
    {
        float sysGain = (float)(SystemVolume / 100.0);
        float micGain = (float)(MicVolume / 100.0);
        if (_meterMixer is not null)
        {
            _meterMixer.SystemGain = sysGain;
            _meterMixer.MicGain = micGain;
        }

        _coordinator.SetAudioGains(sysGain, micGain); // live propagation to an in-progress recording
    }

    private void StartMetering()
    {
        if (_meterMixer is not null || !_isActivePage)
        {
            return;
        }

        if (!SystemAudioEnabled && !MicEnabled)
        {
            return;
        }

        try
        {
            RecMode.Audio.IAudioMixer mixer = _mixerFactory();
            mixer.Start(SystemAudioEnabled, MicEnabled, PerAppAudioTargetPid);
            mixer.SystemGain = (float)(SystemVolume / 100.0);
            mixer.MicGain = (float)(MicVolume / 100.0);
            _meterMixer = mixer;
            _meterTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33), // ≤ 30 Hz (§3.9)
            };
            _meterTimer.Tick += OnMeterTick;
            _meterTimer.Start();
        }
        catch (Exception)
        {
            StopMetering(); // metering is best-effort
        }
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        if (_meterMixer is null)
        {
            return;
        }

        SystemMeter = _meterMixer.SystemLevel.Rms;
        MicMeter = _meterMixer.MicLevel.Rms;
    }

    private void StopMetering()
    {
        if (_meterTimer is not null)
        {
            _meterTimer.Stop();
            _meterTimer.Tick -= OnMeterTick;
            _meterTimer = null;
        }

        _meterMixer?.Dispose();
        _meterMixer = null;
        SystemMeter = 0;
        MicMeter = 0;
    }

    private void RestartMetering()
    {
        StopMetering();
        StartMetering();
    }

    // ---------- Webcam picture-in-picture overlay (Phase 7) ----------

    private WebcamCaptureSource? _previewWebcam;
    private WebcamDevice? _selectedWebcamDevice;
    private bool _webcamEnabled;
    private WebcamOverlayPosition _webcamPosition;
    private int _webcamSizePercent;
    private bool _loadingWebcamDevices;

    public ObservableCollection<WebcamDevice> WebcamDevices { get; } = [];
    public IReadOnlyList<WebcamOverlayPosition> WebcamPositions { get; } =
        [WebcamOverlayPosition.BottomRight, WebcamOverlayPosition.BottomLeft, WebcamOverlayPosition.TopRight, WebcamOverlayPosition.TopLeft];
    public bool HasWebcamDevices => WebcamDevices.Count > 0;

    public bool WebcamEnabled
    {
        get => _webcamEnabled;
        set
        {
            if (!SetProperty(ref _webcamEnabled, value))
            {
                return;
            }

            _settings.Current.WebcamEnabled = value;
            _settings.RequestSave();
            if (value)
            {
                StartWebcamPreview();
            }
            else
            {
                StopWebcamPreview();
            }
        }
    }

    public WebcamDevice? SelectedWebcamDevice
    {
        get => _selectedWebcamDevice;
        set
        {
            if (!SetProperty(ref _selectedWebcamDevice, value) || _loadingWebcamDevices)
            {
                return;
            }

            _settings.Current.WebcamDeviceId = value?.Id;
            _settings.RequestSave();
            if (WebcamEnabled)
            {
                StopWebcamPreview();
                StartWebcamPreview();
            }
        }
    }

    public WebcamOverlayPosition WebcamPosition
    {
        get => _webcamPosition;
        set
        {
            if (!SetProperty(ref _webcamPosition, value))
            {
                return;
            }

            _settings.Current.WebcamPosition = value;
            _settings.RequestSave();
            ApplyWebcamOverlayToPreview();
        }
    }

    public int WebcamSizePercent
    {
        get => _webcamSizePercent;
        set
        {
            if (!SetProperty(ref _webcamSizePercent, value))
            {
                return;
            }

            _settings.Current.WebcamSizePercent = value;
            _settings.RequestSave();
            ApplyWebcamOverlayToPreview();
        }
    }

    private async void LoadWebcamDevices()
    {
        try
        {
            IReadOnlyList<WebcamDevice> devices = await WebcamEnumerator.FindAllAsync();
            if (!_isActivePage)
            {
                return; // navigated away while enumerating
            }

            _loadingWebcamDevices = true;
            try
            {
                WebcamDevices.Clear();
                foreach (WebcamDevice d in devices)
                {
                    WebcamDevices.Add(d);
                }

                string? savedId = _settings.Current.WebcamDeviceId;
                _selectedWebcamDevice = string.IsNullOrEmpty(savedId)
                    ? WebcamDevices.FirstOrDefault()
                    : WebcamDevices.FirstOrDefault(d => d.Id == savedId) ?? WebcamDevices.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedWebcamDevice));
                OnPropertyChanged(nameof(HasWebcamDevices));
            }
            finally
            {
                _loadingWebcamDevices = false;
            }
        }
        catch (Exception)
        {
            // Enumeration is best-effort — leaves WebcamDevices empty; the UI hides "Enable webcam" then.
        }
    }

    private void StartWebcamPreview()
    {
        if (_previewWebcam is not null || _preview is null || !_webcamEnabled || SelectedWebcamDevice is null)
        {
            return;
        }

        StartWebcamPreviewAsync(SelectedWebcamDevice);
    }

    private async void StartWebcamPreviewAsync(WebcamDevice device)
    {
        var webcam = new WebcamCaptureSource();
        try
        {
            await webcam.StartAsync(device.Id);
        }
        catch (Exception)
        {
            return; // camera unavailable/busy — preview still works without the overlay
        }

        if (!_webcamEnabled || _preview is null || !Equals(SelectedWebcamDevice, device))
        {
            webcam.Stop(); // state changed while awaiting activation — discard
            return;
        }

        _previewWebcam = webcam;
        ApplyWebcamOverlayToPreview();
    }

    private void StopWebcamPreview()
    {
        _previewWebcam?.Stop();
        _previewWebcam = null;
        _preview?.SetWebcamOverlay(null, null);
    }

    private void ApplyWebcamOverlayToPreview()
    {
        if (_preview is null || _previewWebcam is null)
        {
            return;
        }

        (int x, int y, int w, int h) = WebcamOverlayLayout.ComputeRect(_preview.Width, _preview.Height, WebcamSizePercent, WebcamPosition);
        _preview.SetWebcamOverlay(_previewWebcam, new RegionRect(x, y, w, h));
    }

    // ---------- Preview lifecycle (§3.9) ----------

    private void StartPreview()
    {
        if (_preview is not null || IsRecording || !_isActivePage)
        {
            return;
        }

        CaptureTarget? target = CurrentTarget();
        if (target is null || !CaptureCapabilities.IsSupported())
        {
            return;
        }

        try
        {
            IPreviewEngine engine = _previewFactory();
            engine.Start(target, _settings.Current.CaptureCursor);
            _previewBuffer = new byte[engine.ByteSize];
            _previewBitmap = new WriteableBitmap(engine.Width, engine.Height, 96, 96, PixelFormats.Bgra32, null);
            engine.FrameAvailable += OnPreviewFrame;
            _preview = engine;
            PreviewImage = _previewBitmap;
            OnPropertyChanged(nameof(HasPreview));
            StartWebcamPreview();
        }
        catch (Exception)
        {
            StopPreview(); // preview is best-effort; the record path still works
        }
    }

    private void StopPreview()
    {
        StopWebcamPreview();

        if (_preview is null)
        {
            return;
        }

        _preview.FrameAvailable -= OnPreviewFrame;
        _preview.Stop();
        _preview.Dispose();
        _preview = null;
        _previewBitmap = null;
        PreviewImage = null;
        OnPropertyChanged(nameof(HasPreview));
    }

    private void RestartPreview()
    {
        if (_preview is null && (!_isActivePage || IsRecording))
        {
            return;
        }

        StopPreview();
        StartPreview();
    }

    private void OnPreviewFrame() => Dispatch(() =>
    {
        if (_preview is null || _previewBitmap is null)
        {
            return;
        }

        if (_preview.TryGetLatestFrame(_previewBuffer))
        {
            var rect = new Int32Rect(0, 0, _previewBitmap.PixelWidth, _previewBitmap.PixelHeight);
            _previewBitmap.WritePixels(rect, _previewBuffer, _preview.Stride, 0);
        }
    });

    // ---------- Recording ----------

    private void ToggleRecord()
    {
        if (_coordinator.IsRecording)
        {
            _coordinator.Stop();
            return;
        }

        StartRecording(withCountdown: true); // interactive start (button/hotkey/tray) honours the countdown setting
    }

    /// <summary>Starts recording without the pre-roll countdown — for CLI automation (<c>--record</c>), which means "now".</summary>
    public void StartRecordingFromCli()
    {
        if (!_coordinator.IsRecording)
        {
            StartRecording(withCountdown: false);
        }
    }

    private void StartRecording(bool withCountdown)
    {
        CaptureTarget? target = CurrentTarget();
        if (target is null || SelectedEncoder is null)
        {
            return;
        }

        StopPreview(); // preview and recording use separate sessions; don't run both (§3.9)

        if (withCountdown)
        {
            int seconds = _settings.Current.CountdownSeconds;
            MonitorInfo? mon = SelectedMonitor ?? Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
            if (seconds > 0 && mon is not null && !_countdown.Run(mon, seconds))
            {
                StartPreview(); // cancelled during countdown; bring preview back
                return;
            }
        }

        bool started = _coordinator.Start(target, SelectedEncoder, SelectedFormat, SelectedFrameRate, Quality);
        if (started)
        {
            IsRecording = true;
            StatusText = "Recording";
            StatsText = "";
        }
        else
        {
            StartPreview(); // start failed; bring preview back
        }
    }

    private void TogglePause()
    {
        if (_coordinator.IsPaused)
        {
            _coordinator.Resume();
        }
        else
        {
            _coordinator.Pause();
        }
    }

    private void OnProgress(RecordingProgress p) => Dispatch(() =>
    {
        IsRecording = p.State is not Core.Recording.RecordingState.Idle and not Core.Recording.RecordingState.Finalizing;
        IsPaused = p.State == Core.Recording.RecordingState.Paused;
        IsHealthy = p.IsHealthy;
        ElapsedText = FormatElapsed(p.Elapsed);
        string stats = $"{p.Fps.ToString("F0", CultureInfo.InvariantCulture)} fps · {p.Mbps.ToString("F1", CultureInfo.InvariantCulture)} Mbps · {FormatBytes(p.FileSizeBytes)}";
        StatsText = IsPaused ? "Paused" : p.IsHealthy ? stats : $"⚠ Can't keep up · {stats}";
        UpdateDiskSpaceText(p.FileSizeBytes);
    });

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F0} MB",
        _ => $"{bytes / 1024} KB",
    };

    private void OnFinished(RecordingResult result) => Dispatch(() =>
    {
        IsRecording = false;
        IsHealthy = true;
        IsAnnotating = false;
        ElapsedText = "00:00";
        StatusText = result.Success
            ? $"Saved {Path.GetFileName(result.OutputPath)}"
            : "Recording ended — check the log";
        StatsText = "";
        UpdateDiskSpaceText(); // back to the "free of total" idle view
        StartPreview(); // resume the live preview
    });

    private static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");

    private static void Dispatch(Action action)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            action();
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(action);
        }
    }
}
