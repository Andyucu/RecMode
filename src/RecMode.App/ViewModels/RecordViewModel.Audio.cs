using System.Collections.ObjectModel;
using RecMode.Capture;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    private RecMode.Audio.IAudioMixer? _meterMixer;
    private System.Windows.Threading.DispatcherTimer? _meterTimer;

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
}
