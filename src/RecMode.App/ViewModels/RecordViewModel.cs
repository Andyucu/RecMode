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
    private readonly ICountdownController _countdown;

    public RecordViewModel(RecordingCoordinator coordinator, IEncoderProbe encoderProbe,
        ISettingsService settings, Func<IPreviewEngine> previewFactory, IRegionPicker regionPicker,
        Func<RecMode.Audio.IAudioMixer> mixerFactory, ScreenshotService screenshots,
        ICountdownController countdown)
    {
        _coordinator = coordinator;
        _encoderProbe = encoderProbe;
        _settings = settings;
        _previewFactory = previewFactory;
        _regionPicker = regionPicker;
        _mixerFactory = mixerFactory;
        _screenshots = screenshots;
        _countdown = countdown;
        _systemAudioEnabled = settings.Current.SystemAudioEnabled;
        _micEnabled = settings.Current.MicrophoneEnabled;
        _systemVolume = settings.Current.SystemVolume;
        _micVolume = settings.Current.MicVolume;

        if (settings.Current.RegionWidth > 0 && settings.Current.RegionHeight > 0)
        {
            _region = new RegionRect(settings.Current.RegionX, settings.Current.RegionY,
                settings.Current.RegionWidth, settings.Current.RegionHeight);
        }

        Formats = [MediaContainer.Mp4, MediaContainer.Mkv, MediaContainer.Mov, MediaContainer.WebM];
        FrameRates = [30, 60, 120];
        _selectedFormat = Formats.Contains(settings.Current.Container) ? settings.Current.Container : MediaContainer.Mp4;
        _selectedFrameRate = FrameRates.Contains(settings.Current.FrameRate) ? settings.Current.FrameRate : 60;
        _quality = Math.Clamp(settings.Current.Quality, 0, 100);

        RecordCommand = new RelayCommand(ToggleRecord, () => CurrentTarget() is not null && SelectedEncoder is not null);
        ChangeRegionCommand = new RelayCommand(() => PickRegion(revertOnCancel: false));
        PauseResumeCommand = new RelayCommand(TogglePause);
        ScreenshotCommand = new RelayCommand(TakeScreenshot, () => CurrentTarget() is not null);
        ToggleAnnotateCommand = new RelayCommand(() => { if (_coordinator.IsRecording) IsAnnotating = !IsAnnotating; });

        _coordinator.ProgressChanged += OnProgress;
        _coordinator.Finished += OnFinished;
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public ObservableCollection<WindowInfo> Windows { get; } = [];
    public ObservableCollection<EncoderInfo> Encoders { get; } = [];
    public IReadOnlyList<MediaContainer> Formats { get; }
    public IReadOnlyList<int> FrameRates { get; }

    public IRelayCommand RecordCommand { get; }
    public IRelayCommand ChangeRegionCommand { get; }
    public IRelayCommand PauseResumeCommand { get; }
    public IRelayCommand ScreenshotCommand { get; }
    public IRelayCommand ToggleAnnotateCommand { get; }

    /// <summary>Captures a still of the current source (F11 / button). Runs on the UI thread.</summary>
    public void TakeScreenshot()
    {
        CaptureTarget? target = CurrentTarget();
        if (target is not null)
        {
            _screenshots.Capture(target);
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

            // On first switch to Region, pick immediately if none stored yet.
            if (_region is null)
            {
                PickRegion(revertOnCancel: true);
            }
            else
            {
                RestartPreview();
            }
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

    public void OnNavigatedTo()
    {
        _isActivePage = true;
        LoadDevices();
        StartPreview();
        StartMetering();
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
            mixer.Start(SystemAudioEnabled, MicEnabled);
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
        }
        catch (Exception)
        {
            StopPreview(); // preview is best-effort; the record path still works
        }
    }

    private void StopPreview()
    {
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
