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

    private MonitorInfo? _selectedMonitor;
    private WindowInfo? _selectedWindow;
    private EncoderInfo? _selectedEncoder;
    private MediaContainer _selectedFormat;
    private int _selectedFrameRate;
    private int _quality;
    private bool _isScreenSource = true;
    private bool _isWindowSource;
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

    public RecordViewModel(RecordingCoordinator coordinator, IEncoderProbe encoderProbe,
        ISettingsService settings, Func<IPreviewEngine> previewFactory)
    {
        _coordinator = coordinator;
        _encoderProbe = encoderProbe;
        _settings = settings;
        _previewFactory = previewFactory;

        Formats = [MediaContainer.Mp4, MediaContainer.Mkv];
        FrameRates = [30, 60, 120];
        _selectedFormat = settings.Current.Container is MediaContainer.Mkv ? MediaContainer.Mkv : MediaContainer.Mp4;
        _selectedFrameRate = FrameRates.Contains(settings.Current.FrameRate) ? settings.Current.FrameRate : 60;
        _quality = Math.Clamp(settings.Current.Quality, 0, 100);

        RecordCommand = new RelayCommand(ToggleRecord, () => CurrentTarget() is not null && SelectedEncoder is not null);

        _coordinator.ProgressChanged += OnProgress;
        _coordinator.Finished += OnFinished;
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public ObservableCollection<WindowInfo> Windows { get; } = [];
    public ObservableCollection<EncoderInfo> Encoders { get; } = [];
    public IReadOnlyList<MediaContainer> Formats { get; }
    public IReadOnlyList<int> FrameRates { get; }

    public IRelayCommand RecordCommand { get; }

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

    public bool ShowWindowPicker => IsWindowSource;

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

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }
    public string StatsText { get => _statsText; private set => SetProperty(ref _statsText, value); }

    public void OnNavigatedTo()
    {
        _isActivePage = true;
        LoadDevices();
        StartPreview();
    }

    public void OnNavigatedFrom()
    {
        _isActivePage = false;
        StopPreview();
    }

    /// <summary>Called by the shell on minimize/restore to honour the §3.9 "nothing runs when hidden" rule.</summary>
    public void SetWindowMinimized(bool minimized)
    {
        if (minimized)
        {
            StopPreview();
        }
        else if (_isActivePage && !IsRecording)
        {
            StartPreview();
        }
    }

    private CaptureTarget? CurrentTarget()
    {
        if (IsWindowSource)
        {
            return SelectedWindow is null ? null : CaptureTarget.FromWindow(SelectedWindow);
        }

        return SelectedMonitor is null ? null : CaptureTarget.FromMonitor(SelectedMonitor);
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

        CaptureTarget? target = CurrentTarget();
        if (target is null || SelectedEncoder is null)
        {
            return;
        }

        StopPreview(); // preview and recording use separate sessions; don't run both (§3.9)
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

    private void OnProgress(RecordingProgress p) => Dispatch(() =>
    {
        IsRecording = p.State is not Core.Recording.RecordingState.Idle and not Core.Recording.RecordingState.Finalizing;
        ElapsedText = FormatElapsed(p.Elapsed);
        StatsText = $"{p.Fps.ToString("F0", CultureInfo.InvariantCulture)} fps · {p.FramesWritten} frames";
    });

    private void OnFinished(RecordingResult result) => Dispatch(() =>
    {
        IsRecording = false;
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
