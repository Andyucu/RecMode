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

    /// <summary>The PID to narrow system-audio capture to, or null for the whole system ("All apps" sentinel
    /// has <c>ProcessId == 0</c>, which also maps to null here).</summary>
    private int? PerAppAudioTargetPid => SelectedPerAppAudioTarget is { ProcessId: > 0 } t ? t.ProcessId : null;

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
                ToggleMicMuteCommand.NotifyCanExecuteChanged();
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

    private bool _isMicMuted;

    /// <summary>Global-hotkey (default Ctrl+Shift+M, remappable in Settings) / floating-toolbar mic mute
    /// toggle. A layer on top of <see cref="MicVolume"/> rather than zeroing it — the volume slider keeps
    /// showing the user's real preferred level while muted instead of visibly jumping to 0 and back, and
    /// muting never touches (or persists) the saved volume setting.</summary>
    public bool IsMicMuted
    {
        get => _isMicMuted;
        private set { if (SetProperty(ref _isMicMuted, value)) OnPropertyChanged(nameof(MicMuteButtonText)); }
    }

    public string MicMuteButtonText => IsMicMuted ? "Unmute mic" : "Mute mic";

    /// <summary>Only acts while actually recording with the mic enabled — matches the request's "while
    /// recording" scope and avoids a confusing "muted" indicator with nothing to mute.</summary>
    private void ToggleMicMute()
    {
        if (!_coordinator.IsRecording || !MicEnabled)
        {
            return;
        }

        IsMicMuted = !IsMicMuted;
        ApplyGains();
    }

    private void ApplyGains()
    {
        float sysGain = (float)(SystemVolume / 100.0);
        float micGain = IsMicMuted ? 0f : (float)(MicVolume / 100.0);
        if (_meterMixer is not null)
        {
            _meterMixer.SystemGain = sysGain;
            _meterMixer.MicGain = micGain;
        }

        _coordinator.SetAudioGains(sysGain, micGain); // live propagation to an in-progress recording
    }

    private void StartMetering()
    {
        // §3.9: same combined guard as StartPreview — see SetWindowVisible.
        if (!_isActivePage || IsWindowMinimized || !IsWindowVisible || !_hostsPreviewSurfaces)
        {
            return;
        }

        if (!SystemAudioEnabled && !MicEnabled)
        {
            return;
        }

        EnsureMeterTimer();

        // While recording, the coordinator's mixer is already capturing and computing peak/RMS on every
        // audio callback. Opening the meter mixer too meant two independent WASAPI graphs — each holding a
        // loopback *and* a mic capture client, each resampling every callback, and with per-app audio
        // selected, two process-loopback sessions on the same PID — for the entire length of every recording,
        // to display numbers the first mixer had already computed. The timer below reads from the coordinator
        // instead for the duration (§3.9).
        if (_coordinator.IsRecording)
        {
            StopMeterMixer();
            return;
        }

        if (_meterMixer is not null)
        {
            return;
        }

        try
        {
            RecMode.Audio.IAudioMixer mixer = _mixerFactory();
            // Assigned before Start() rather than after: if Start() (or a gain setter) throws, the catch
            // below calls StopMetering(), which disposes via _meterMixer — assigning only on success left
            // _meterMixer null on a mid-Start() failure, so the mixer's already-opened WASAPI capture
            // client(s) were never disposed (a leak repeated on every nav to Record / audio-toggle flip).
            _meterMixer = mixer;
            mixer.Start(SystemAudioEnabled, MicEnabled, PerAppAudioTargetPid, meteringOnly: true);
            mixer.SystemGain = (float)(SystemVolume / 100.0);
            mixer.MicGain = (float)(MicVolume / 100.0);
        }
        catch (Exception)
        {
            StopMetering(); // metering is best-effort
        }
    }

    private void EnsureMeterTimer()
    {
        if (_meterTimer is not null)
        {
            return;
        }

        _meterTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33), // ≤ 30 Hz (§3.9)
        };
        _meterTimer.Tick += OnMeterTick;
        _meterTimer.Start();
    }

    private void OnMeterTick(object? sender, EventArgs e)
    {
        // Source depends on what's running: the recording's own mixer while recording, the metering-only
        // mixer otherwise. Never both — see StartMetering.
        if (_coordinator.IsRecording)
        {
            SystemMeter = _coordinator.SystemAudioLevel.Rms;
            MicMeter = _coordinator.MicAudioLevel.Rms;
            return;
        }

        if (_meterMixer is null)
        {
            SystemMeter = 0;
            MicMeter = 0;
            return;
        }

        SystemMeter = _meterMixer.SystemLevel.Rms;
        MicMeter = _meterMixer.MicLevel.Rms;
    }

    /// <summary>Tears down the metering-only mixer, leaving the UI timer alone — used when a recording takes
    /// over as the level source.</summary>
    private void StopMeterMixer()
    {
        _meterMixer?.Dispose();
        _meterMixer = null;
    }

    private void StopMetering()
    {
        if (_meterTimer is not null)
        {
            _meterTimer.Stop();
            _meterTimer.Tick -= OnMeterTick;
            _meterTimer = null;
        }

        StopMeterMixer();
        SystemMeter = 0;
        MicMeter = 0;
    }

    /// <summary>Re-evaluates which mixer the meters should be reading from. Called when a recording starts or
    /// stops, so the metering-only mixer is released for the recording's duration and re-opened afterward.</summary>
    private void RefreshMeteringSource()
    {
        if (_coordinator.IsRecording)
        {
            StopMeterMixer();
        }
        else
        {
            StartMetering();
        }
    }

    private void RestartMetering()
    {
        StopMetering();
        StartMetering();
    }
}
