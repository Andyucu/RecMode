using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
/// Split into partial-class files by concern: this file covers construction, source selection, device
/// loading, and page lifecycle; see RecordViewModel.Profiles.cs (recording profiles), .Audio.cs (audio
/// mixer/metering), .Webcam.cs (webcam PIP overlay), .Preview.cs (live preview), and .Recording.cs
/// (start/stop/pause + progress reporting) for the rest.
/// </summary>
public sealed partial class RecordViewModel : ObservableObject, INavigationAware
{
    private readonly RecordingCoordinator _coordinator;
    private readonly IEncoderProbe _encoderProbe;
    private readonly ISettingsService _settings;
    private readonly Func<IPreviewEngine> _previewFactory;
    private readonly IRegionPicker _regionPicker;
    private readonly Func<RecMode.Audio.IAudioMixer> _mixerFactory;

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

    private readonly ScreenshotService _screenshots;
    private readonly IScreenshotFlash _screenshotFlash;
    private readonly ICountdownController _countdown;
    private readonly IProfileNamePrompt _profilePrompt;
    private readonly RecMode.Core.Infrastructure.IAppPaths _paths;
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
        // "All Displays" has no single monitor to size the region picker's overlay against — a region is
        // always relative to one real screen, so fall back to the primary monitor in that case.
        MonitorInfo? mon = SelectedMonitor is { IsAllDisplays: false } m ? m : Monitors.FirstOrDefault(x => x.IsPrimary && !x.IsAllDisplays);
        if (mon is null)
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
        LoadPerAppAudioTargets();
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

        if (SelectedMonitor is not { } selected)
        {
            return null;
        }

        return selected.IsAllDisplays ? CaptureTarget.FromAllDisplays(Monitors.Where(m => !m.IsAllDisplays).ToList())
            : CaptureTarget.FromMonitor(selected);
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
        IReadOnlyList<MonitorInfo> realMonitors = CaptureCapabilities.EnumerateMonitors();
        foreach (MonitorInfo m in realMonitors)
        {
            Monitors.Add(m);
        }
        if (realMonitors.Count > 1)
        {
            // "Full screen (per display + all displays)" (plan §1) — only meaningful with 2+ real monitors.
            RegionRect bounds = CaptureTarget.FromAllDisplays(realMonitors).VirtualDesktopBounds!.Value;
            Monitors.Add(new MonitorInfo
            {
                Handle = nint.Zero,
                DisplayName = "All Displays",
                DeviceName = "",
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                IsAllDisplays = true,
            });
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

    // Internal (rather than private) so these are directly unit-testable.
    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F0} MB",
        _ => $"{bytes / 1024} KB",
    };

    internal static string FormatElapsed(TimeSpan t) =>
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
