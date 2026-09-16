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
    private bool _isWebcamSource;
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
    private readonly IAudioDevicePrompt _audioDevicePrompt;
    private readonly RecMode.Core.Infrastructure.IAppPaths _paths;
    private readonly IErrorReporter _errors;
    private string _diskSpaceText = "";

    public RecordViewModel(RecordingCoordinator coordinator, IEncoderProbe encoderProbe,
        ISettingsService settings, Func<IPreviewEngine> previewFactory, IRegionPicker regionPicker,
        IWindowPicker windowPicker, Func<RecMode.Audio.IAudioMixer> mixerFactory, ScreenshotService screenshots,
        IScreenshotFlash screenshotFlash, ICountdownController countdown, IProfileNamePrompt profilePrompt,
        IAudioDevicePrompt audioDevicePrompt, RecMode.Core.Infrastructure.IAppPaths paths, IErrorReporter errors)
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
        _audioDevicePrompt = audioDevicePrompt;
        _paths = paths;
        _errors = errors;
        _systemAudioEnabled = settings.Current.SystemAudioEnabled;
        _micEnabled = settings.Current.MicrophoneEnabled;
        _separateAudioTracks = settings.Current.SeparateAudioTracks;
        _micNoiseSuppressionEnabled = settings.Current.MicNoiseSuppression;
        _micNoiseSuppressionStrength = settings.Current.MicNoiseSuppressionStrength;
        _systemVolume = settings.Current.SystemVolume;
        _micVolume = settings.Current.MicVolume;
        _webcamEnabled = settings.Current.WebcamEnabled;
        _webcamPosition = settings.Current.WebcamPosition;
        _webcamSizePercent = settings.Current.WebcamSizePercent;
        _followWindowEnabled = settings.Current.FollowWindow;
        _redactAreaEnabled = settings.Current.RedactAreaEnabled;
        if (settings.Current.RedactAreaWidth > 0 && settings.Current.RedactAreaHeight > 0)
        {
            _redactArea = new RegionRect(settings.Current.RedactAreaX, settings.Current.RedactAreaY,
                settings.Current.RedactAreaWidth, settings.Current.RedactAreaHeight);
        }

        if (settings.Current.RegionWidth > 0 && settings.Current.RegionHeight > 0)
        {
            _region = new RegionRect(settings.Current.RegionX, settings.Current.RegionY,
                settings.Current.RegionWidth, settings.Current.RegionHeight);
        }

        Formats = [MediaContainer.Mp4, MediaContainer.Mkv, MediaContainer.Mov, MediaContainer.WebM];
        FrameRates = [10, 15, 20, 25, 30, 60, 120];
        _selectedFormat = Formats.Contains(settings.Current.Container) ? settings.Current.Container : MediaContainer.Mkv;
        // A stale "separate tracks" setting from when a different container was selected can't be honored on
        // MP4/WebM; clear it rather than showing a checked-but-disabled toggle.
        if (_separateAudioTracks && !SeparateAudioTracksAvailable)
        {
            _separateAudioTracks = false;
            settings.Current.SeparateAudioTracks = false;
        }
        _selectedFrameRate = FrameRates.Contains(settings.Current.FrameRate) ? settings.Current.FrameRate : 30;
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
        ToggleMicMuteCommand = new RelayCommand(ToggleMicMute, () => _coordinator.IsRecording && MicEnabled);
        ToggleHighlightClicksCommand = new RelayCommand(() => IsHighlightingClicks = !IsHighlightingClicks);
        ToggleMicNoiseSuppressionCommand = new RelayCommand(() => IsMicNoiseSuppressionEnabled = !IsMicNoiseSuppressionEnabled);
        AddChapterCommand = new RelayCommand(AddChapter);
        SaveProfileCommand = new RelayCommand(SaveProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => CanDeleteProfile);
        ChooseRedactAreaCommand = new RelayCommand(ChooseRedactArea);
        ClearRedactAreaCommand = new RelayCommand(ClearRedactArea, () => HasRedactArea);
        ToggleRedactionCommand = new RelayCommand(ToggleRedaction);
        SetQualityPresetCommand = new RelayCommand<string>(v => { if (int.TryParse(v, out int q)) Quality = q; });
        SelectAudioDevicesCommand = new RelayCommand(SelectAudioDevices);

        LoadProfiles();

        _coordinator.ProgressChanged += OnProgress;
        _coordinator.Finished += OnFinished;
        _screenshots.Captured += OnScreenshotCaptured;
        // Settings' "Encoding defaults" page (container/codec/backend) writes straight to _settings.Current,
        // but SelectedFormat/SelectedEncoder here were only ever read once — in this constructor and the
        // one-time LoadDevices() — so changing the default on the Settings page while the Record screen was
        // already open (this view model is a DI singleton, constructed once for the app's lifetime) silently
        // had no effect until a full restart. Worse, touching the Record screen's own Format combo at all
        // would then write the *stale* cached value straight back over whatever the user had just set in
        // Settings. Never resynced while actually recording — the container/encoder of a file being written
        // right now can't change out from under it.
        settings.SettingsChanged += OnSettingsChangedRefreshEncodingDefaults;
        // Unlike encoding defaults (above), HighlightClicks CAN change mid-recording — that's the whole point
        // of also exposing it from the floating toolbar (ClickHighlightService reacts live via its own
        // SettingsChanged subscription). IsHighlightingClicks reads straight from _settings.Current rather
        // than caching a field, so this just needs to repaint the binding whenever the value changes from
        // anywhere else (the Settings screen, or a future second surface) — including while recording.
        settings.SettingsChanged += (_, _) => OnPropertyChanged(nameof(IsHighlightingClicks));
        // Live display-topology changes (dock/undock, resolution change, RDP connect): refresh the monitor
        // picker even when the user is parked on the Record screen and never navigates away — the plain
        // nav-time refresh misses that case. SystemEvents raises this on a non-UI thread and often in bursts
        // during one topology transition, so the handler coalesces into a single debounced refresh.
        // RecordViewModel is an app-lifetime DI singleton, so this subscription deliberately lives for the
        // process; the handler no-ops until the first LoadDevices() has populated anything.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private int _displayChangeRefreshPending;

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _displayChangeRefreshPending, 1) != 0)
        {
            return;
        }

        Dispatch(() =>
        {
            Interlocked.Exchange(ref _displayChangeRefreshPending, 0);
            if (_devicesLoaded)
            {
                LoadMonitors();
            }
        });
    }

    private void OnSettingsChangedRefreshEncodingDefaults(object? sender, EventArgs e)
    {
        if (_coordinator.IsRecording)
        {
            return;
        }

        if (Formats.Contains(_settings.Current.Container))
        {
            SelectedFormat = _settings.Current.Container;
        }

        if (_devicesLoaded)
        {
            EncoderInfo? matching = Encoders.FirstOrDefault(
                e => e.Codec == _settings.Current.Codec && e.Backend == _settings.Current.Backend);
            if (matching is not null)
            {
                SelectedEncoder = matching;
            }
        }
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
    public IRelayCommand ToggleMicMuteCommand { get; }
    public IRelayCommand ToggleHighlightClicksCommand { get; }

    /// <summary>Toolbar toggle for microphone noise suppression — flips the same persisted setting the Record
    /// screen's audio card edits, and applies it to a recording in progress.</summary>
    public IRelayCommand ToggleMicNoiseSuppressionCommand { get; }

    /// <summary>Stamps a chapter marker on the recording in progress (toolbar button / global hotkey). No-op
    /// when nothing is recording.</summary>
    public IRelayCommand AddChapterCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand DeleteProfileCommand { get; }

    /// <summary>Sets Quality to a named snap-point value (Web/Balanced/Archive), so users can land on a
    /// sensible value without dragging — the same anchors <see cref="FfmpegArgsBuilder.QualityTier"/> names.</summary>
    public IRelayCommand<string> SetQualityPresetCommand { get; }

    /// <summary>Opens the "System audio devices" picker — see <see cref="SelectAudioDevices"/>.</summary>
    public IRelayCommand SelectAudioDevicesCommand { get; }

    /// <summary>Captures a still of the current source (F11 / button). The actual WGC grab, PNG encode, and
    /// file write now run off the UI thread (see <see cref="ScreenshotService.Capture"/>'s doc comment) —
    /// previously this method ran synchronously on the UI thread, which for a full-resolution 4K/ultrawide
    /// recording froze the recording toolbar's repaint and every low-level mouse/keyboard hook behind it
    /// (F11 itself is delivered via a global hotkey on this same UI-thread message pump) for the whole
    /// capture+encode+write duration.</summary>
    public void TakeScreenshot()
    {
        CaptureTarget? target = CurrentTarget();
        if (target is not null)
        {
            _ = System.Threading.Tasks.Task.Run(() => _screenshots.Capture(target));
            if (SelectedMonitor is { } mon)
            {
                _screenshotFlash.Flash(mon);
            }
        }
    }

    // Runs off the UI thread (ScreenshotService.Capture is invoked via Task.Run) — must marshal before
    // touching bound properties. Only overwrites StatusText while idle: while recording, StatusText is
    // showing "Recording"/health state, which a screenshot taken mid-recording shouldn't clobber.
    private void OnScreenshotCaptured(string path) => Dispatch(() =>
    {
        LastScreenshotPath = path;
        if (!_coordinator.IsRecording)
        {
            StatusText = $"Screenshot saved: {Path.GetFileName(path)}";
        }
    });

    public bool IsScreenSource
    {
        get => _isScreenSource;
        set
        {
            if (SetProperty(ref _isScreenSource, value) && value)
            {
                OnPropertyChanged(nameof(ShowWindowPicker));
                OnPropertyChanged(nameof(ShowWebcamOverlayCard));
                RestartPreview();
                NotifyCaptureCommandsCanExecuteChanged();
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
                OnPropertyChanged(nameof(ShowWebcamOverlayCard));
                RestartPreview();
                NotifyCaptureCommandsCanExecuteChanged();
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
            OnPropertyChanged(nameof(ShowWebcamOverlayCard));
            NotifyCaptureCommandsCanExecuteChanged();
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

    /// <summary>Webcam as the recording source itself (distinct from the picture-in-picture overlay further
    /// down the Record screen, which composites a webcam onto another source instead of replacing it) —
    /// records <see cref="SelectedWebcamDevice"/> directly via <see cref="RecMode.Capture.Webcam.WebcamCaptureEngine"/>.
    /// The PIP overlay card is hidden while this is selected (see <see cref="ShowWebcamOverlayCard"/>) since
    /// overlaying a webcam onto itself doesn't mean anything.</summary>
    public bool IsWebcamSource
    {
        get => _isWebcamSource;
        set
        {
            if (SetProperty(ref _isWebcamSource, value) && value)
            {
                OnPropertyChanged(nameof(ShowWebcamOverlayCard));
                RestartPreview();
                NotifyCaptureCommandsCanExecuteChanged();
            }
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
        IsModalPromptOpen = true;
        RegionRect? picked;
        try
        {
            picked = _regionPicker.Pick(mon);
        }
        finally
        {
            _selectingRegion = false;
            IsModalPromptOpen = false;
        }

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
            NotifyCaptureCommandsCanExecuteChanged();
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
    public bool ShowWebcamOverlayCard => !IsWebcamSource;

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
        set { if (SetProperty(ref _selectedMonitor, value)) { RestartPreview(); NotifyCaptureCommandsCanExecuteChanged(); OnPropertyChanged(nameof(QualityLabel)); } }
    }

    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set { if (SetProperty(ref _selectedWindow, value)) { RestartPreview(); NotifyCaptureCommandsCanExecuteChanged(); OnPropertyChanged(nameof(QualityLabel)); } }
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
                _settings.RequestSave();
                OnPropertyChanged(nameof(HardwareBadge));
                OnPropertyChanged(nameof(QualityLabel));
                NotifyCaptureCommandsCanExecuteChanged();
            }
        }
    }

    public MediaContainer SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (!SetProperty(ref _selectedFormat, value))
            {
                return;
            }

            _settings.Current.Container = value;
            // Separate audio tracks are MKV/MOV-only; switching to MP4/WebM must not leave the (now inert)
            // option ticked, or the persisted setting would claim something the container can't do.
            if (_separateAudioTracks && !SeparateAudioTracksAvailable)
            {
                SeparateAudioTracks = false;
            }
            OnPropertyChanged(nameof(SeparateAudioTracksAvailable));
            _settings.RequestSave();
        }
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
            int kbps = SelectedEncoder is { } enc0
                ? FfmpegArgsBuilder.EstimateTypicalKbps(w, h, SelectedFrameRate, Quality, enc0.Codec, enc0.IsHardware)
                : FfmpegArgsBuilder.EstimateTypicalKbps(w, h, SelectedFrameRate, Quality);
            double mbPerMinute = kbps * 60.0 / 8000.0; // kbit/s -> MB/min (decimal MB, "roughly how big")
            int crf = SelectedEncoder is { } enc ? FfmpegArgsBuilder.EffectiveQualityValue(enc, Quality) : FfmpegArgsBuilder.QualityToCrf(Quality);
            return $"{FfmpegArgsBuilder.QualityTier(Quality)} · ~{mbPerMinute:0.#} MB/min · CRF {crf}";
        }
    }

    private CaptureTarget? _sizeLabelCacheTarget;
    private (int Width, int Height) _sizeLabelCache;

    /// <summary>Best-effort source resolution for <see cref="QualityLabel"/>'s size estimate — the current
    /// capture target's raw size (not the post-<c>CaptureSizing</c> encode size, close enough for an estimate),
    /// falling back to a common 1080p assumption when no target is selected yet or its size can't be read.
    /// Cached per-target: for a Monitor/Window source, <see cref="CaptureCapabilities.TryGetSourceSize"/>
    /// creates an actual WGC <c>GraphicsCaptureItem</c> just to read its size — genuinely expensive COM work
    /// to redo on every <c>QualityLabel</c> read (bound in XAML, re-evaluated on every property-changed
    /// notification touching it), when the resolved size for an unchanged target never changes.</summary>
    private static readonly (int Width, int Height) DefaultSizeLabelResolution = (1920, 1080);

    private (int Width, int Height) EstimatedResolutionForSizeLabel()
    {
        CaptureTarget? target = CurrentTarget(refreshFollowedWindow: false);
        if (target is null)
        {
            return DefaultSizeLabelResolution;
        }

        if (_sizeLabelCacheTarget is not null && _sizeLabelCacheTarget.Equals(target))
        {
            return _sizeLabelCache;
        }

        // A Webcam target is deliberately not probed here. TryGetSourceSize activates the camera for real
        // (MediaCapture.InitializeAsync + teardown, ~100-300 ms, blocking) — unacceptable from a data-bound
        // getter that re-evaluates on every Quality slider tick, which locked the UI solid for the length of
        // a drag whenever the camera was slow or held by another app. The recording path still probes
        // properly during preflight, where blocking is expected; this label is documented as a rough anchor,
        // so the common 720p webcam mode is a good enough basis for it.
        (int Width, int Height) resolved =
            target.Kind == CaptureKind.Webcam ? (1280, 720)
            : CaptureCapabilities.TryGetSourceSize(target, out int w, out int h) ? (w, h)
            : DefaultSizeLabelResolution;

        // Cached unconditionally, including the fallback. Caching only on success meant a failing probe
        // (camera busy, window closed, monitor disconnected) was retried on every single read forever.
        _sizeLabelCacheTarget = target;
        _sizeLabelCache = resolved;
        return resolved;
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
                OnPropertyChanged(nameof(CanToggleRedaction));
                // The toolbar's redact light reflects whether redaction actually got armed at start; it's
                // reset on stop so a stale "hidden" indicator can't outlive the recording.
                IsRedacting = value && RedactAreaEnabled && _coordinator.CaptureSupportsRedaction;
                if (value)
                {
                    ChapterCount = 0; // the counter is per-recording
                }
                ToggleMicMuteCommand.NotifyCanExecuteChanged();
                // Hand the meters over to (or back from) the recording's own mixer, so only one WASAPI
                // capture graph is ever open at a time — see RefreshMeteringSource.
                RefreshMeteringSource();
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

    private int _chapterCount;

    /// <summary>Chapters stamped during the current recording (toolbar/hotkey). Shown as a small counter next
    /// to the toolbar's chapter button so the hotkey has visible feedback; reset when a recording starts.</summary>
    public int ChapterCount
    {
        get => _chapterCount;
        private set { if (SetProperty(ref _chapterCount, value)) OnPropertyChanged(nameof(HasChapters)); }
    }

    public bool HasChapters => _chapterCount > 0;

    private void AddChapter()
    {
        if (_coordinator.AddChapter())
        {
            ChapterCount++;
        }
    }

    /// <summary>The "Highlight mouse clicks" setting (§Phase 8), also exposed here — not just on the Settings
    /// screen — so it can be flipped on/off from the floating recording toolbar mid-recording, for pinpointing
    /// something on screen for a moment and then turning it back off, without leaving whatever's being
    /// recorded to go find the toggle in Settings. Reads straight from <c>_settings.Current</c> rather than
    /// caching a field, since <see cref="ClickHighlightService"/> (which actually shows/hides the ripple
    /// overlay) reacts to the same underlying setting from a second, independent surface (the Settings
    /// screen) that this must stay in sync with — see the constructor's <c>SettingsChanged</c> subscription.</summary>
    public bool IsHighlightingClicks
    {
        get => _settings.Current.HighlightClicks;
        set
        {
            if (_settings.Current.HighlightClicks == value)
            {
                return;
            }

            _settings.Current.HighlightClicks = value;
            _settings.RequestSave();
            OnPropertyChanged();
        }
    }

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

    /// <summary>Called by <see cref="ClickHighlightService"/> whenever it shows/hides the click-ripple
    /// overlay, so the coordinator can substitute a Region-equivalent capture for Window sources — same
    /// reasoning as <see cref="IsAnnotating"/>'s coordinator notification, just for a different overlay (see
    /// <see cref="RecordingCoordinator.SetClickHighlightActive"/>).</summary>
    public void NotifyClickHighlightActive(bool active) => _coordinator.SetClickHighlightActive(active);

    /// <summary>Called by <see cref="KeystrokeVisualizerService"/> — see
    /// <see cref="NotifyClickHighlightActive"/>.</summary>
    public void NotifyKeystrokeVisualizerActive(bool active) => _coordinator.SetKeystrokeVisualizerActive(active);

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

    private string? _lastScreenshotPath;
    /// <summary>Full path of the most recently saved screenshot. Mirrors <see cref="LastRecordingPath"/>'s
    /// "jump to Library" convention (see <see cref="ShellViewModel"/>).</summary>
    public string? LastScreenshotPath { get => _lastScreenshotPath; private set => SetProperty(ref _lastScreenshotPath, value); }

    /// <summary>How much room is left on the output drive — "{used} of {total}" while recording, "{free} free of {total}" at rest.</summary>
    public string DiskSpaceText { get => _diskSpaceText; private set => SetProperty(ref _diskSpaceText, value); }

    // Cache for the "used of total" recording display below — a drive's total capacity is effectively
    // constant for the life of a recording, so there's no need to re-query it on every progress tick.
    private string? _cachedTotalRoot;
    private long _cachedTotalBytes;

    /// <summary>Refreshes <see cref="DiskSpaceText"/> against the output folder's drive. Best-effort — a bad path or unready drive just clears the text.</summary>
    private void UpdateDiskSpaceText(long recordingBytes = 0)
    {
        try
        {
            string outputDir = _paths.ResolveUserPath(_settings.Current.OutputFolder) ?? _paths.RecordingsDirectory;
            string? root = Path.GetPathRoot(Path.GetFullPath(outputDir));
            if (root is null)
            {
                DiskSpaceText = "";
                return;
            }

            if (recordingBytes > 0)
            {
                // This branch is driven by RecordingCoordinator.ProgressChanged (≤ 4 Hz) for the whole
                // duration of a recording — real DriveInfo reads are actual syscalls, and on a mapped
                // network-share output folder a stalled server would otherwise stall the UI thread four
                // times a second. Only re-read the drive when the root actually changes.
                if (!string.Equals(root, _cachedTotalRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var driveForTotal = new DriveInfo(root);
                    if (!driveForTotal.IsReady)
                    {
                        DiskSpaceText = "";
                        return;
                    }

                    _cachedTotalRoot = root;
                    _cachedTotalBytes = driveForTotal.TotalSize;
                }

                DiskSpaceText = $"{FormatBytes(recordingBytes)} of {FormatBytes(_cachedTotalBytes)}";
                return;
            }

            // Idle "free of total" view: only refreshed on page navigation and when a recording finishes,
            // never on the hot progress-tick path, so a fresh read here is fine.
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                DiskSpaceText = "";
                return;
            }

            _cachedTotalRoot = root;
            _cachedTotalBytes = drive.TotalSize;
            DiskSpaceText = $"{FormatBytes(drive.AvailableFreeSpace)} free of {FormatBytes(drive.TotalSize)}";
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
        RefreshRedactionFromSettings();
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
        else
        {
            TryResumeAfterVisibilityChange();
        }
    }

    /// <summary>Called whenever the top-level window actually hosting this page (<c>ShellWindow</c> or
    /// <c>CompactWindow</c> — both share this one <see cref="RecordViewModel"/> instance) is shown or hidden
    /// via <c>Window.Show()</c>/<c>Hide()</c>, as distinct from OS-level minimize (<see cref="SetWindowMinimized"/>).
    /// Two real §3.9 gaps this closes that minimize alone didn't cover: a <c>--tray</c> launch, where no window
    /// is ever shown at all (so preview/metering must never start in the first place, not just stop once
    /// something notices); and <c>ShellPresenter</c> swapping the active shell layout (Sidebar/TopTab ↔
    /// Compact), which hides the previous window without minimizing it — previously left its preview/meters
    /// running for the rest of the session. Defaults to not-visible: nothing is shown until a window's own
    /// <c>Show()</c> call fires <c>IsVisibleChanged</c>.
    /// <para><paramref name="hostsPreviewSurfaces"/> — <c>ShellWindow</c> and <c>CompactWindow</c> are not
    /// interchangeable here: <c>CompactWindow.xaml</c> binds none of <c>PreviewImage</c>/<c>HasPreview</c>/
    /// <c>SystemMeter</c>/<c>MicMeter</c> (verified — it has only the source tiles and audio enable toggles,
    /// no meter bars or preview image at all), so starting a full WGC/D3D11 preview session plus live WASAPI
    /// metering while Compact is the shown window burns §3.9's exact budget for zero observable benefit. Pass
    /// <c>true</c> from a window that actually displays them, <c>false</c> from one that doesn't — a window
    /// reporting itself hidden always stops preview/metering regardless of this flag's last value.</para></summary>
    public void SetWindowVisible(bool visible, bool hostsPreviewSurfaces = true)
    {
        IsWindowVisible = visible;
        _hostsPreviewSurfaces = hostsPreviewSurfaces;
        if (!visible)
        {
            StopPreview();
            StopMetering();
        }
        else
        {
            TryResumeAfterVisibilityChange();
        }
    }

    private bool _hostsPreviewSurfaces = true;

    /// <summary>Shared by <see cref="SetWindowMinimized"/> and <see cref="SetWindowVisible"/>: re-evaluate
    /// whether preview/metering should actually (re)start now, given both flags plus the existing
    /// active-page/recording state. <see cref="StartPreview"/>/<see cref="StartMetering"/> re-check the full
    /// combined condition themselves, so calling this from either flag's setter is safe regardless of which
    /// order the two flags settle in.</summary>
    private void TryResumeAfterVisibilityChange()
    {
        if (!PreviewEligibility.CanRun(_isActivePage, _isWindowMinimized, _isWindowVisible, _hostsPreviewSurfaces))
        {
            return;
        }

        if (!IsRecording)
        {
            StartPreview();
        }
        StartMetering();
    }

    private bool _isWindowMinimized;
    public bool IsWindowMinimized { get => _isWindowMinimized; private set => SetProperty(ref _isWindowMinimized, value); }

    private bool _isWindowVisible;
    public bool IsWindowVisible { get => _isWindowVisible; private set => SetProperty(ref _isWindowVisible, value); }

    // Defaults true: a headless/test construction (no WPF Window ever calls SetWindowActive) must not
    // silently disable anything gated on this — see SourceContourService's use, which only NARROWS an
    // existing "visible" check, so a stale-true default here is safe (it just means "no foreground
    // information available", not "definitely foreground").
    private bool _isWindowActive = true;
    /// <summary>OS foreground-activation state, as distinct from <see cref="IsWindowVisible"/> (shown vs.
    /// hidden) and <see cref="IsWindowMinimized"/> — a window can be fully visible, not minimized, and still
    /// not be the foreground window (the user alt-tabbed away, or it's just sitting on another monitor).
    /// <see cref="Services.SourceContourService"/> needs this distinction specifically: registering a global
    /// Esc hotkey while RecMode isn't the foreground app hijacks Esc for every other application on the
    /// machine, which "visible and not minimized" alone doesn't rule out.</summary>
    public bool IsWindowActive { get => _isWindowActive; private set => SetProperty(ref _isWindowActive, value); }

    /// <summary>Called by the shell's <c>Window.Activated</c>/<c>Deactivated</c> handlers.</summary>
    public void SetWindowActive(bool active) => IsWindowActive = active;

    private bool _isActivePageObservable;
    /// <summary>Mirrors the private <c>_isActivePage</c> field (set in <see cref="OnNavigatedTo"/>/
    /// <see cref="OnNavigatedFrom"/>) as a real notifying property, for <see cref="Services.SourceContourService"/>
    /// to observe. Kept as a separate field rather than converting the existing one, so this is a pure addition
    /// with zero risk to the established preview/metering lifecycle logic that already reads the plain field.</summary>
    public bool IsActivePage { get => _isActivePageObservable; private set => SetProperty(ref _isActivePageObservable, value); }

    /// <summary>True while a RecMode-owned modal (the region picker, the Save-profile name prompt) is open via
    /// a blocking <c>ShowDialog()</c>. <see cref="Services.SourceContourService"/> observes this to suspend its
    /// global "clear region" Esc hotkey for the duration — without it, that hotkey (registered whenever a
    /// Region source is merely selected, not just while actively dragging one) silently steals the Esc
    /// keypress the modal's own Cancel handling needs, so pressing Esc to dismiss the region picker or the
    /// Save-profile dialog instead cleared the region and reverted to Screen while the modal stayed open.</summary>
    public bool IsModalPromptOpen { get => _modalPromptOpenObservable; private set => SetProperty(ref _modalPromptOpenObservable, value); }
    private bool _modalPromptOpenObservable;

    /// <summary>The capture target the Record screen currently has selected (Screen/Window/Region, whichever
    /// tile is active) — the same resolution <see cref="RecordCommand"/> itself uses to decide what a press of
    /// Record would capture. Exposed read-only for <see cref="Services.SourceContourService"/>, which draws a
    /// live outline around it so it's always obvious what's actually about to be/being recorded.</summary>
    public CaptureTarget? CurrentSelectionTarget => CurrentTarget(refreshFollowedWindow: false);

    private CaptureTarget? CurrentTarget(bool refreshFollowedWindow = true)
    {
        if (IsWebcamSource)
        {
            return SelectedWebcamDevice is { } device ? CaptureTarget.FromWebcam(device.Id, device.DisplayName) : null;
        }

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
        // Encoders are probed exactly once per process: the trial-encode sweep is expensive and the machine's
        // encoder hardware doesn't change mid-session. Monitors deliberately are NOT under this guard — see
        // LoadMonitors.
        if (!_devicesLoaded)
        {
            Encoders.Clear();
            foreach (EncoderInfo e in _encoderProbe.GetAvailableEncoders())
            {
                Encoders.Add(e);
            }
            SelectedEncoder = PickDefaultEncoder();
            _autoSelectedEncoder = SelectedEncoder; // remembered so ApplyRecommendedEncoder can tell "still the default" from "user picked this"
            _devicesLoaded = true;
        }

        LoadMonitors();
    }

    /// <summary>Re-enumerates displays and rebuilds the picker list, preserving the user's selection when the
    /// monitor is still present. Unlike the encoder probe this runs on every device load (Record-screen
    /// navigation, <c>--tray</c>/<c>--record</c> headless load) AND on live display-topology changes while
    /// the app is open (<see cref="OnDisplaySettingsChanged"/>, debounced), because the monitor set genuinely
    /// changes while the app runs: dock a laptop or plug in a display and it becomes selectable without an
    /// app restart or any navigation; unplug one and a stale selection would otherwise hold a dead HMONITOR
    /// that silently captures nothing; and "All Displays" (only meaningful with 2+ real monitors) appears/
    /// disappears with the real count. Windows already refresh this way on every load — monitors were simply
    /// left out.</summary>
    private void LoadMonitors()
    {
        List<MonitorInfo> fresh = [.. CaptureCapabilities.EnumerateMonitors()];
        if (fresh.Count > 1)
        {
            // "Full screen (per display + all displays)" (plan §1) — only meaningful with 2+ real monitors.
            RegionRect bounds = CaptureTarget.FromAllDisplays(fresh).VirtualDesktopBounds!.Value;
            fresh.Add(new MonitorInfo
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

        // Unchanged fast path (the common case on every Record-screen visit): don't touch the collection or
        // the selection at all, so no binding churn and no preview restart fires. RecordInfo is a record, so
        // value equality covers handle/geometry/HDR state — a re-enumeration that found exactly the same
        // monitors (the usual case) compares equal element-for-element.
        if (Monitors.Count == fresh.Count)
        {
            bool identical = true;
            for (int i = 0; i < fresh.Count; i++)
            {
                if (!MonitorInfoMatches(Monitors[i], fresh[i]))
                {
                    identical = false;
                    break;
                }
            }

            if (identical)
            {
                return;
            }
        }

        MonitorInfo? previous = SelectedMonitor;
        Monitors.Clear();
        foreach (MonitorInfo m in fresh)
        {
            Monitors.Add(m);
        }

        SelectedMonitor = ResolveMonitorSelection(previous);
    }

    /// <summary>Value equality for the unchanged-list fast path. The synthetic "All Displays" entry has a
    /// zero handle, so it can't be identified by handle alone — compare identity plus the bounding box.</summary>
    private static bool MonitorInfoMatches(MonitorInfo a, MonitorInfo b)
    {
        if (a.IsAllDisplays != b.IsAllDisplays)
        {
            return false;
        }

        return a.IsAllDisplays
            ? a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height
            : a == b;
    }

    /// <summary>Maps a previous selection onto the freshly enumerated list. Real monitors are matched by
    /// friendly name first, then device path — the label is the most stable cross-reconnect identity, while
    /// device paths (<c>\\.\DISPLAYn</c>) renumber when the topology changes, so matching on the path first
    /// could silently re-point the selection to a different physical panel after a replug even when an exact
    /// name match exists — then by handle; falling back to the primary monitor when the selected one is gone.
    /// An "All Displays" selection keeps that entry only if 2+ real monitors are still present.</summary>
    private MonitorInfo? ResolveMonitorSelection(MonitorInfo? previous)
    {
        if (previous is null)
        {
            return Monitors.FirstOrDefault(m => m.IsPrimary && !m.IsAllDisplays) ?? Monitors.FirstOrDefault();
        }

        if (previous.IsAllDisplays)
        {
            return Monitors.FirstOrDefault(m => m.IsAllDisplays)
                ?? Monitors.FirstOrDefault(m => m.IsPrimary && !m.IsAllDisplays)
                ?? Monitors.FirstOrDefault();
        }

        MonitorInfo? resolved =
            Monitors.FirstOrDefault(m => !m.IsAllDisplays && string.Equals(m.DisplayName, previous.DisplayName, StringComparison.Ordinal)) ??
            Monitors.FirstOrDefault(m => !m.IsAllDisplays && string.Equals(m.DeviceName, previous.DeviceName, StringComparison.OrdinalIgnoreCase)) ??
            Monitors.FirstOrDefault(m => !m.IsAllDisplays && m.Handle == previous.Handle && previous.Handle != nint.Zero);

        return resolved
            ?? Monitors.FirstOrDefault(m => m.IsPrimary && !m.IsAllDisplays)
            ?? Monitors.FirstOrDefault();
    }

    private void LoadWindows()
    {
        Windows.Clear();
        foreach (WindowInfo w in CaptureCapabilities.EnumerateWindows())
        {
            Windows.Add(w);
        }

        WindowInfo? next = SelectedWindow is null
            ? Windows.FirstOrDefault()
            : WindowFollowResolver.Resolve(SelectedWindow, Windows) ?? Windows.FirstOrDefault();

        // Assigns the backing field directly rather than going through the SelectedWindow property: every
        // caller of LoadWindows() already restarts the preview itself right after calling this (either
        // explicitly or via the IsWindowSource setter), so going through the property too meant every
        // IsWindowSource-flip-to-Window rebuilt the whole capture/preview pipeline twice in a row.
        if (!Equals(next, _selectedWindow))
        {
            _selectedWindow = next;
            OnPropertyChanged(nameof(SelectedWindow));
            OnPropertyChanged(nameof(QualityLabel));
            NotifyCaptureCommandsCanExecuteChanged();
        }
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

    private EncoderInfo? _autoSelectedEncoder;

    /// <summary>
    /// Applies the first-run encoder benchmark's recommendation to the live Record screen. Writing it to
    /// settings alone wasn't enough: <see cref="LoadDevices"/> → <see cref="PickDefaultEncoder"/> has almost
    /// always already run by the time the background benchmark finishes (the Record screen is deliberately
    /// usable in ~2s), and nothing re-selected afterward — so the "recommended default" only ever took
    /// effect on the *second* launch, which defeats the point of a first-run benchmark.
    /// <para>Declines if the user has since picked an encoder themselves, or if a recording is already under
    /// way — a benchmark result must never change the encoder out from under either.</para>
    /// </summary>
    internal void ApplyRecommendedEncoder(VideoCodec codec, EncoderBackend backend)
    {
        if (IsRecording || !ReferenceEquals(SelectedEncoder, _autoSelectedEncoder))
        {
            return;
        }

        EncoderInfo? match = Encoders.FirstOrDefault(e => e.Codec == codec && e.Backend == backend);
        if (match is not null)
        {
            SelectedEncoder = match;
            _autoSelectedEncoder = match;
        }
    }

    /// <summary>Refreshes both capture commands' enabled state — they share the same "is there a resolved
    /// source (and, for Record, an encoder)" precondition, but only <see cref="RecordCommand"/> used to be
    /// notified at each of these call sites. <see cref="ScreenshotCommand"/>'s button could then show enabled
    /// while <see cref="CurrentTarget"/> had actually gone null (e.g. switching to Region before any region is
    /// picked) — clicking it silently did nothing — or stay disabled after becoming valid again until some
    /// unrelated re-query happened to run.</summary>
    private void NotifyCaptureCommandsCanExecuteChanged()
    {
        RecordCommand.NotifyCanExecuteChanged();
        ScreenshotCommand.NotifyCanExecuteChanged();
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

    /// <summary>Same marshaling as <see cref="Dispatch"/> but at <see cref="System.Windows.Threading.DispatcherPriority.Render"/>
    /// instead of the default Normal — for high-frequency, non-critical UI work (preview frame writes, up to
    /// ~30/sec) that shouldn't compete with input/layout at the same priority as state changes users are
    /// actively waiting on (recording progress, start/stop).</summary>
    private static void DispatchLowPriority(Action action)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            action();
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, action);
        }
    }
}
