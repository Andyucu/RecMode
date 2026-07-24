using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Errors;
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
    private readonly IWindowPicker _windowPicker;
    private readonly Func<RecMode.Audio.IAudioMixer> _mixerFactory;

    private MonitorInfo? _selectedMonitor;
    private WindowInfo? _selectedWindow;
    private EncoderInfo? _selectedEncoder;
    private MediaContainer _selectedFormat;
    private int _selectedFrameRate;
    private int _quality;
    private double _brightness;
    private RegionRect? _region;
    private bool _isScreenSource = true;
    private bool _isWindowSource;
    private bool _isRegionSource;
    private bool _followWindowEnabled;
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
    private readonly IErrorReporter _errors;
    private string _diskSpaceText = "";

    public RecordViewModel(RecordingCoordinator coordinator, IEncoderProbe encoderProbe,
        ISettingsService settings, Func<IPreviewEngine> previewFactory, IRegionPicker regionPicker,
        IWindowPicker windowPicker, Func<RecMode.Audio.IAudioMixer> mixerFactory, ScreenshotService screenshots,
        IScreenshotFlash screenshotFlash, ICountdownController countdown, IProfileNamePrompt profilePrompt,
        RecMode.Core.Infrastructure.IAppPaths paths, IErrorReporter errors)
    {
        _coordinator = coordinator;
        _encoderProbe = encoderProbe;
        _settings = settings;
        _previewFactory = previewFactory;
        _regionPicker = regionPicker;
        _windowPicker = windowPicker;
        _mixerFactory = mixerFactory;
        _screenshots = screenshots;
        _screenshotFlash = screenshotFlash;
        _countdown = countdown;
        _profilePrompt = profilePrompt;
        _paths = paths;
        _errors = errors;
        _systemAudioEnabled = settings.Current.SystemAudioEnabled;
        _micEnabled = settings.Current.MicrophoneEnabled;
        _systemVolume = settings.Current.SystemVolume;
        _micVolume = settings.Current.MicVolume;
        _webcamEnabled = settings.Current.WebcamEnabled;
        _webcamPosition = settings.Current.WebcamPosition;
        _webcamSizePercent = settings.Current.WebcamSizePercent;
        _followWindowEnabled = settings.Current.FollowWindow;

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
        _brightness = Math.Clamp(settings.Current.Brightness, -100, 100);

        RecordCommand = new RelayCommand(ToggleRecord,
            () => CurrentTarget(refreshFollowedWindow: false) is not null && SelectedEncoder is not null);
        ChangeRegionCommand = new RelayCommand(() => PickRegion(revertOnCancel: false));
        PickWindowCommand = new RelayCommand(PickWindowWithMouse);
        PauseResumeCommand = new RelayCommand(TogglePause);
        ScreenshotCommand = new RelayCommand(TakeScreenshot, () => CurrentTarget(refreshFollowedWindow: false) is not null);
        ToggleAnnotateCommand = new RelayCommand(() => { if (_coordinator.IsRecording) IsAnnotating = !IsAnnotating; });
        ToggleManualZoomCommand = new RelayCommand(ToggleManualZoom);
        SaveProfileCommand = new RelayCommand(SaveProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => CanDeleteProfile);
        SetQualityPresetCommand = new RelayCommand<string>(v => { if (int.TryParse(v, out int q)) Quality = q; });

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
    public IRelayCommand PickWindowCommand { get; }
    public IRelayCommand PauseResumeCommand { get; }
    public IRelayCommand ScreenshotCommand { get; }
    public IRelayCommand ToggleAnnotateCommand { get; }
    public IRelayCommand ToggleManualZoomCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand DeleteProfileCommand { get; }

    /// <summary>Sets Quality to a named snap-point value (Web/Balanced/Archive), so users can land on a
    /// sensible value without dragging — the same anchors <see cref="FfmpegArgsBuilder.QualityTier"/> names.</summary>
    public IRelayCommand<string> SetQualityPresetCommand { get; }

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
                OnPropertyChanged(nameof(ShowFollowWindow));
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
            OnPropertyChanged(nameof(QualityLabel));
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

    /// <summary>Live-updates the pending Region selection while <see cref="Services.SourceContourService"/>'s
    /// on-screen outline is being dragged (not recording — a recording in progress instead goes through
    /// <see cref="RecordingCoordinator.SetBaseRect"/>, which doesn't touch this pending-selection state at
    /// all). Deliberately doesn't persist to settings or restart the preview on every drag tick — see
    /// <see cref="CompleteRegionDrag"/> for that, once the gesture actually ends.</summary>
    public void UpdateRegionFromDrag(RegionRect monitorLocalRect)
    {
        if (IsRegionSource)
        {
            _region = monitorLocalRect;
        }
    }

    /// <summary>Persists the region the contour was just dragged to and restarts the preview to match — called
    /// once when the drag gesture ends, not on every tick (see <see cref="UpdateRegionFromDrag"/>).</summary>
    public void CompleteRegionDrag()
    {
        if (_region is not { } r)
        {
            return;
        }

        _settings.Current.RegionX = r.X;
        _settings.Current.RegionY = r.Y;
        _settings.Current.RegionWidth = r.Width;
        _settings.Current.RegionHeight = r.Height;
        _settings.RequestSave();
        OnPropertyChanged(nameof(RegionLabel));
        OnPropertyChanged(nameof(QualityLabel));
        RestartPreview();
    }

    /// <summary>Clears the current Region selection (reverts to Screen) — the global Esc hotkey while the
    /// contour is up in its "selected, not recording" state calls this (see
    /// <see cref="Services.SourceContourService"/>). No-ops while recording — the source is locked for the
    /// duration of a recording regardless, same as every other Record-screen control.</summary>
    public void ClearRegionSelection()
    {
        if (IsRegionSource && !IsRecording)
        {
            RevertToScreen();
        }
    }

    /// <summary>Shows the point-and-click "pick a window with the mouse" overlay (alternative to the Windows
    /// combo box). Also flips on Window source, so this works as a one-click entry point even before the
    /// Window tile is selected.</summary>
    private void PickWindowWithMouse()
    {
        WindowInfo? picked = _windowPicker.Pick();
        if (picked is null)
        {
            return;
        }

        LoadWindows(); // refresh so the picked window (and anything opened since the last load) is present
        WindowInfo? match = Windows.FirstOrDefault(w => w.Handle == picked.Handle);
        if (match is null)
        {
            // Rare: the picked window closed, or otherwise dropped out of the filtered enumeration, between
            // the overlay resolving it and this refresh — fall back to what was actually picked.
            match = picked;
            Windows.Add(match);
        }

        IsWindowSource = true;
        SelectedWindow = match;
    }

    public string RegionLabel => _region is { } r ? $"Region {r.Width} × {r.Height}" : "No region selected";
    public bool ShowWindowPicker => IsWindowSource;
    public bool ShowFollowWindow => IsWindowSource;
    public bool ShowRegionInfo => IsRegionSource;

    public bool FollowWindowEnabled
    {
        get => _followWindowEnabled;
        set
        {
            if (SetProperty(ref _followWindowEnabled, value))
            {
                _settings.Current.FollowWindow = value;
                _settings.RequestSave();
                RestartPreview();
            }
        }
    }

    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set { if (SetProperty(ref _selectedMonitor, value)) { RestartPreview(); RecordCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(QualityLabel)); } }
    }

    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set { if (SetProperty(ref _selectedWindow, value)) { RestartPreview(); RecordCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(QualityLabel)); } }
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
                OnPropertyChanged(nameof(QualityLabel));
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
        set
        {
            if (SetProperty(ref _selectedFrameRate, value))
            {
                _settings.Current.FrameRate = value;
                _settings.RequestSave();
                OnPropertyChanged(nameof(QualityLabel));
            }
        }
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

    /// <summary>Friendlier than a bare CRF number: a qualitative tier + an estimated file size, with the raw
    /// CRF/CQ/QP number (the actual value the selected encoder will use — <see cref="FfmpegArgsBuilder.EffectiveQualityValue"/>,
    /// not just the uncalibrated curve) kept alongside for technical users. The size estimate uses the
    /// currently selected source's resolution when known, falling back to a 1080p assumption otherwise — it's
    /// a rough anchor ("roughly how big"), not a precise prediction (see <see cref="FfmpegArgsBuilder.EstimateTypicalKbps"/>).</summary>
    public string QualityLabel
    {
        get
        {
            (int w, int h) = EstimatedResolutionForSizeLabel();
            int kbps = FfmpegArgsBuilder.EstimateTypicalKbps(w, h, SelectedFrameRate, Quality);
            double mbPerMinute = kbps * 60.0 / 8000.0; // kbit/s -> MB/min (decimal MB, "roughly how big")
            int crf = SelectedEncoder is { } enc ? FfmpegArgsBuilder.EffectiveQualityValue(enc, Quality) : FfmpegArgsBuilder.QualityToCrf(Quality);
            return $"{FfmpegArgsBuilder.QualityTier(Quality)} · ~{mbPerMinute:0.#} MB/min · CRF {crf}";
        }
    }

    /// <summary>Best-effort source resolution for <see cref="QualityLabel"/>'s size estimate — the current
    /// capture target's raw size (not the post-<c>CaptureSizing</c> encode size, close enough for an estimate),
    /// falling back to a common 1080p assumption when no target is selected yet or its size can't be read.</summary>
    private (int Width, int Height) EstimatedResolutionForSizeLabel()
    {
        CaptureTarget? target = CurrentTarget(refreshFollowedWindow: false);
        if (target is not null && CaptureCapabilities.TryGetSourceSize(target, out int w, out int h))
        {
            return (w, h);
        }
        return (1920, 1080);
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, value))
            {
                _settings.Current.Brightness = value;
                _settings.RequestSave();
                _preview?.SetBrightness(value);
                if (IsRecording)
                {
                    _coordinator.SetBrightness(value);
                }
                OnPropertyChanged(nameof(BrightnessLabel));
            }
        }
    }

    public string BrightnessLabel => Brightness == 0 ? "0" : $"{Brightness:+0;-0}";

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
    /// <summary>True while draw-on-screen annotation is active (only meaningful during a recording, Phase 8).
    /// Notifies the coordinator so it can substitute a Region-equivalent capture for Window sources, which
    /// WGC's per-window capture otherwise can't see overlay ink drawn on top of (see
    /// <see cref="RecordingCoordinator.SetAnnotating"/>).</summary>
    public bool IsAnnotating
    {
        get => _isAnnotating;
        private set
        {
            if (SetProperty(ref _isAnnotating, value))
            {
                _coordinator.SetAnnotating(value);
            }
        }
    }

    /// <summary>Turns annotation off (called by the overlay on Esc and when the recording ends).</summary>
    public void StopAnnotating() => IsAnnotating = false;

    private bool _isManualZooming;
    /// <summary>True while the toolbar's manual "Zoom"/"Exit Zoom" button has an area zoomed in. Distinct from
    /// Smart auto-zoom (click-triggered) — this is a user-picked area that stays zoomed until they exit it,
    /// rather than an eased click-follow. Both share the same GPU crop mechanism
    /// (<see cref="RecordingCoordinator.SetZoomTarget"/>), so only one is meaningful at a time in practice.</summary>
    public bool IsManualZooming
    {
        get => _isManualZooming;
        private set { if (SetProperty(ref _isManualZooming, value)) OnPropertyChanged(nameof(ZoomButtonText)); }
    }

    public string ZoomButtonText => IsManualZooming ? "Exit Zoom" : "Zoom";

    private void ToggleManualZoom()
    {
        if (!_coordinator.IsRecording)
        {
            return;
        }

        if (IsManualZooming)
        {
            StopManualZoom();
            return;
        }

        // Monitor/Region sources only — same scope cut as Smart auto-zoom (RecordingCoordinator.ComputeZoomRect):
        // Window/All-Displays sources don't have a fixed monitor origin to resolve a picked rect against.
        if (ActiveCaptureTarget is not { } target || target.Kind is CaptureKind.Window or CaptureKind.AllDisplays)
        {
            _errors.Warn("record.zoom-unsupported", "Zoom isn't available for this recording.",
                "Manual zoom only works for Screen and Region sources.");
            return;
        }

        if (!_coordinator.CaptureSupportsZoom)
        {
            _errors.Warn("record.zoom-unsupported", "Zoom isn't available for this recording.",
                "Screen capture fell back to a compatibility mode on this system, which can't apply the zoom effect.");
            return;
        }

        MonitorInfo? mon = Monitors.FirstOrDefault(m => m.Handle == target.Handle);
        if (mon is null)
        {
            return;
        }

        // Reuses the Region-source picker overlay, excluded from capture (this recording is already running)
        // so the drag-select chrome itself never shows up in the output.
        if (_regionPicker.Pick(mon, excludeFromCapture: true) is not { } picked)
        {
            return; // cancelled (Esc) — stay unzoomed
        }

        // A Region-source recording's actual bounds are a sub-rect of the monitor the picker covers; clamp
        // the pick into those bounds rather than reject a pick made outside the recorded area outright.
        RegionRect bounds = target.Region ?? new RegionRect(0, 0, mon.Width, mon.Height);
        _coordinator.SetZoomTarget(AutoZoomMath.Clamp(picked, bounds));
        IsManualZooming = true;
    }

    /// <summary>Exits manual zoom (called by the toolbar button when already zoomed, and by the global Esc
    /// hotkey via <see cref="Services.ManualZoomService"/>).</summary>
    public void StopManualZoom()
    {
        if (!IsManualZooming)
        {
            return;
        }

        _coordinator.SetZoomTarget(null);
        IsManualZooming = false;
    }

    /// <summary>The capture target actually being recorded, fixed at the moment recording started (not
    /// re-evaluated from the Record screen's current selection). Null when not recording. Lets the
    /// draw-on-screen overlay cover exactly what's being captured — the selected monitor/region/window —
    /// instead of always the primary monitor.</summary>
    public CaptureTarget? ActiveCaptureTarget { get; private set; }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }
    public string StatsText { get => _statsText; private set => SetProperty(ref _statsText, value); }

    private string? _lastRecordingPath;
    /// <summary>Full path of the most recently finished recording, or null if none yet / a new recording has
    /// started since. Backs the title bar's "click the status to jump to that recording in the Library" link.</summary>
    public string? LastRecordingPath { get => _lastRecordingPath; private set => SetProperty(ref _lastRecordingPath, value); }

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
        IsActivePage = true;
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
        IsActivePage = false;
        StopPreview();
        StopMetering();
    }

    /// <summary>Called by the shell on minimize/restore to honour the §3.9 "nothing runs when hidden" rule.</summary>
    public void SetWindowMinimized(bool minimized)
    {
        IsWindowMinimized = minimized;
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

    private bool _isWindowMinimized;
    public bool IsWindowMinimized { get => _isWindowMinimized; private set => SetProperty(ref _isWindowMinimized, value); }

    private bool _isActivePageObservable;
    /// <summary>Mirrors the private <c>_isActivePage</c> field (set in <see cref="OnNavigatedTo"/>/
    /// <see cref="OnNavigatedFrom"/>) as a real notifying property, for <see cref="Services.SourceContourService"/>
    /// to observe. Kept as a separate field rather than converting the existing one, so this is a pure addition
    /// with zero risk to the established preview/metering lifecycle logic that already reads the plain field.</summary>
    public bool IsActivePage { get => _isActivePageObservable; private set => SetProperty(ref _isActivePageObservable, value); }

    /// <summary>The capture target the Record screen currently has selected (Screen/Window/Region, whichever
    /// tile is active) — the same resolution <see cref="RecordCommand"/> itself uses to decide what a press of
    /// Record would capture. Exposed read-only for <see cref="Services.SourceContourService"/>, which draws a
    /// live outline around it so it's always obvious what's actually about to be/being recorded.</summary>
    public CaptureTarget? CurrentSelectionTarget => CurrentTarget(refreshFollowedWindow: false);

    private CaptureTarget? CurrentTarget(bool refreshFollowedWindow = true)
    {
        if (IsRegionSource)
        {
            return _region is { } r && SelectedMonitor is { } mon
                ? CaptureTarget.FromRegion(mon, r)
                : null;
        }

        if (IsWindowSource)
        {
            WindowInfo? window = refreshFollowedWindow ? CurrentWindow() : SelectedWindow;
            return window is null ? null : CaptureTarget.FromWindow(window);
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
        SelectedWindow = SelectedWindow is null
            ? Windows.FirstOrDefault()
            : WindowFollowResolver.Resolve(SelectedWindow, Windows) ?? Windows.FirstOrDefault();
    }

    private WindowInfo? CurrentWindow()
    {
        if (SelectedWindow is not { } current)
        {
            return null;
        }

        if (!FollowWindowEnabled)
        {
            return current;
        }

        IReadOnlyList<WindowInfo> live = CaptureCapabilities.EnumerateWindows();
        WindowInfo? resolved = WindowFollowResolver.Resolve(current, live);
        if (resolved is null)
        {
            return null;
        }

        if (resolved != current)
        {
            Windows.Clear();
            foreach (WindowInfo w in live)
            {
                Windows.Add(w);
            }

            _selectedWindow = resolved;
            OnPropertyChanged(nameof(SelectedWindow));
        }

        return resolved;
    }

    private EncoderInfo? PickDefaultEncoder()
    {
        EncoderInfo? saved = Encoders.FirstOrDefault(e =>
            e.Codec == _settings.Current.Codec && e.Backend == _settings.Current.Backend);
        return saved
            ?? Encoders.FirstOrDefault(e => e is { Codec: VideoCodec.H264, IsHardware: true })
            ?? Encoders.FirstOrDefault(e => e.FfmpegId == "libx264")
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
