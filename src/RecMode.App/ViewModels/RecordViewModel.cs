using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.App.ViewModels;

/// <summary>
/// The Record screen (plan Phase 1 functional core): pick a display, encoder, format, fps and quality,
/// then Record → a valid file. Live preview and audio arrive in Phases 2/4. Drives the shared
/// <see cref="RecordingCoordinator"/>.
/// </summary>
public sealed class RecordViewModel : ObservableObject, INavigationAware
{
    private readonly RecordingCoordinator _coordinator;
    private readonly IEncoderProbe _encoderProbe;
    private readonly ISettingsService _settings;

    private MonitorInfo? _selectedMonitor;
    private EncoderInfo? _selectedEncoder;
    private MediaContainer _selectedFormat;
    private int _selectedFrameRate;
    private int _quality;
    private bool _isRecording;
    private string _statusText = "Ready";
    private string _elapsedText = "00:00";
    private string _statsText = "";
    private bool _devicesLoaded;

    public RecordViewModel(RecordingCoordinator coordinator, IEncoderProbe encoderProbe, ISettingsService settings)
    {
        _coordinator = coordinator;
        _encoderProbe = encoderProbe;
        _settings = settings;

        Formats = [MediaContainer.Mp4, MediaContainer.Mkv];
        FrameRates = [30, 60, 120];
        _selectedFormat = settings.Current.Container is MediaContainer.Mkv ? MediaContainer.Mkv : MediaContainer.Mp4;
        _selectedFrameRate = FrameRates.Contains(settings.Current.FrameRate) ? settings.Current.FrameRate : 60;
        _quality = Math.Clamp(settings.Current.Quality, 0, 100);

        RecordCommand = new RelayCommand(ToggleRecord, () => SelectedMonitor is not null && SelectedEncoder is not null);

        _coordinator.ProgressChanged += OnProgress;
        _coordinator.Finished += OnFinished;
    }

    public ObservableCollection<MonitorInfo> Monitors { get; } = [];
    public ObservableCollection<EncoderInfo> Encoders { get; } = [];
    public IReadOnlyList<MediaContainer> Formats { get; }
    public IReadOnlyList<int> FrameRates { get; }

    public IRelayCommand RecordCommand { get; }

    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set { if (SetProperty(ref _selectedMonitor, value)) RecordCommand.NotifyCanExecuteChanged(); }
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

    public void OnNavigatedTo() => LoadDevices();

    public void OnNavigatedFrom()
    {
        // No live preview yet (Phase 2); nothing to tear down while idle. An active recording keeps running.
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

    private EncoderInfo? PickDefaultEncoder()
    {
        // Honour saved preference, else prefer a hardware H.264, else the first available.
        EncoderInfo? saved = Encoders.FirstOrDefault(e =>
            e.Codec == _settings.Current.Codec && e.Backend == _settings.Current.Backend);
        return saved
            ?? Encoders.FirstOrDefault(e => e is { Codec: VideoCodec.H264, IsHardware: true })
            ?? Encoders.FirstOrDefault(e => e.Codec == VideoCodec.H264)
            ?? Encoders.FirstOrDefault();
    }

    private void ToggleRecord()
    {
        if (_coordinator.IsRecording)
        {
            _coordinator.Stop();
            return;
        }

        if (SelectedMonitor is null || SelectedEncoder is null)
        {
            return;
        }

        bool started = _coordinator.Start(SelectedMonitor, SelectedEncoder, SelectedFormat, SelectedFrameRate, Quality);
        if (started)
        {
            IsRecording = true;
            StatusText = "Recording";
            StatsText = "";
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
