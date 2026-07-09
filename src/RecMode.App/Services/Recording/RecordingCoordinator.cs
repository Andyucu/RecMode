using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using RecMode.Audio;
using RecMode.Capture;
using RecMode.Capture.Webcam;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Recording;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using RecMode.Encoding.Ffmpeg;
using Serilog;

namespace RecMode.App.Services;

/// <summary>
/// Ties the recording state machine to the capture engine and the ffmpeg session, running the CFR pacing
/// loop that pulls the latest NV12 frame and writes it to the encoder pipe (the Tier-1 path proven in the
/// Phase 0.5 spike). Owns lifecycle/teardown (§3.9) and maps failures to the error taxonomy (§3.6).
/// Singleton; every UI surface drives this one object.
/// </summary>
public sealed class RecordingCoordinator : IDisposable
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    private readonly Func<ICaptureEngine> _captureFactory;
    private readonly IFfmpegLocator _ffmpeg;
    private readonly IAppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly IErrorReporter _errors;
    private readonly RecordingStateMachine _stateMachine;

    private ICaptureEngine? _capture;
    private WebcamCaptureSource? _webcamCapture;
    private FfmpegRecordingSession? _session;
    private Thread? _pacer;
    private volatile bool _stopRequested;

    // Guards Finalize() against running twice — Stop() (UI thread) and the pacer thread's own fatal-error
    // path (HandleFatalPipeBreak) can both reach a finalize attempt if the encoder dies right as the user
    // clicks Stop; only the first to claim the lock actually finalizes/transitions state/raises Finished.
    private readonly Lock _finalizeLock = new();
    private bool _finalizeStarted;

    // Safe-recording remux (record to MKV, convert to MP4 on stop).
    private string? _ffmpegPath;
    private bool _safeRemux;
    private string _finalPath = "";
    private string _recordingPath = "";

    // Audio.
    private readonly Func<IAudioMixer> _mixerFactory;
    private IAudioMixer? _mixer;
    private Thread? _audioThread;
    private CancellationTokenSource? _audioStop;

    private readonly IEncoderProbe _encoderProbe;
    private readonly EncoderFallbackChain _fallbackChain;
    private readonly IPowerStatus _power;
    private readonly IDiskSpeedProbe _diskSpeed;
    private readonly RecMode.Core.Library.ILibraryIndex _libraryIndex;

    // Metadata snapshot for the library index (captured at Start, written on successful finalize).
    private string _metaSource = "", _metaCodec = "", _metaContainer = "";
    private int _metaWidth, _metaHeight, _metaFps, _metaQuality;
    private bool _metaSystemAudioEnabled, _metaMicEnabled;

    // Auto-split (§3.3 Phase 3 tail): roll to a new segment file once the current one hits the size threshold.
    private bool _autoSplitEnabled;
    private long _autoSplitThresholdBytes;
    private int _segmentIndex = 1;
    private string _outputDir = "";
    private string _baseFileName = "";
    private List<EncoderInfo>? _encoderChain;
    private FfmpegJob? _jobTemplate;

    // Mid-stream hw→sw Degraded fallback (§3.6 / Phase 3 tail): the encoder actually in use for the current
    // segment, and whether a downgrade has already been attempted this recording (once per recording).
    private EncoderInfo? _activeEncoder;
    private bool _downgradeAttempted;
    private volatile bool _testForceDowngrade; // test-only seam for --selftest-downgrade; see AttemptDowngrade

    // Test-only seam (--selftest-webcam): injects a synthetic frame source in place of a real
    // WebcamCaptureSource, so the GPU picture-in-picture compositing can be verified without camera hardware.
    private IWebcamFrameSource? _testForcedWebcamSource;
    internal void TestForceWebcamSource(IWebcamFrameSource source) => _testForcedWebcamSource = source;

    public RecordingCoordinator(
        Func<ICaptureEngine> captureFactory,
        IFfmpegLocator ffmpeg,
        IAppPaths paths,
        ISettingsService settings,
        IErrorReporter errors,
        RecordingStateMachine stateMachine,
        IEncoderProbe encoderProbe,
        Func<IAudioMixer> mixerFactory,
        IPowerStatus power,
        IDiskSpeedProbe diskSpeed,
        RecMode.Core.Library.ILibraryIndex libraryIndex)
    {
        _captureFactory = captureFactory;
        _ffmpeg = ffmpeg;
        _paths = paths;
        _settings = settings;
        _errors = errors;
        _stateMachine = stateMachine;
        _encoderProbe = encoderProbe;
        _fallbackChain = new EncoderFallbackChain(encoderProbe);
        _power = power;
        _diskSpeed = diskSpeed;
        _libraryIndex = libraryIndex;
        _mixerFactory = mixerFactory;
    }

    public RecordingState State => _stateMachine.State;
    public bool IsRecording => _stateMachine.IsActive;

    /// <summary>Throttled progress (≤ 4 Hz). Raised on the pacing thread — the VM marshals to the dispatcher.</summary>
    public event Action<RecordingProgress>? ProgressChanged;

    /// <summary>Raised on stop/finalize with the outcome. Also raised (Success=false) on a fatal mid-recording failure.</summary>
    public event Action<RecordingResult>? Finished;

    /// <summary>Result of a passed pre-flight — everything the rest of <see cref="Start"/> needs from it.</summary>
    private readonly record struct PreflightResult(string OutputDir, string FfmpegPath, int SourceWidth, int SourceHeight);

    /// <summary>Starts a recording. Returns false (with a reported BlockingError) if pre-flight fails.</summary>
    public bool Start(CaptureTarget target, EncoderInfo encoder, MediaContainer container, int fps, int quality)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(encoder);
        if (_stateMachine.IsActive)
        {
            return false;
        }

        if (RunPreflight(target, encoder, container) is not { } pf)
        {
            return false;
        }

        try
        {
            (int dstW, int dstH) = CaptureSizing.Resolve(pf.SourceWidth, pf.SourceHeight, encoder);

            _capture = _captureFactory();
            _capture.Start(target, dstW, dstH, _settings.Current.CaptureCursor);

            // Webcam picture-in-picture overlay (Phase 7): best-effort — a missing/busy camera warns but
            // never blocks the recording. Runs synchronously before capture is considered "started" so the
            // very first frame already carries the overlay, not just frames after it warms up.
            SetupWebcamOverlay(dstW, dstH);

            (FfmpegJob job, bool audioEnabled) = PrepareSession(
                target, encoder, container, pf.OutputDir, pf.FfmpegPath, dstW, dstH, fps, quality);

            if (audioEnabled)
            {
                StartAudioMixer();
            }

            // Encoder fallback chain (§3.6): selected → same-codec other backend → any hw H.264 → libx264.
            _encoderChain = _fallbackChain.Build(encoder);
            _jobTemplate = job;
            _session = TryStartAnyEncoder(_encoderChain, job, _capture.Nv12ByteSize);
            if (_session is null)
            {
                _errors.Block("record.no-encoder", "No video encoder could be started.",
                    "Try a different encoder in Settings.");
                SafeTeardown();
                return false;
            }

            _stateMachine.StartRecording();
            _stopRequested = false;
            _finalizeStarted = false;
            _lastSizeBytes = 0;
            _lastSizeTicks = 0;
            _targetFps = fps;
            _encoderBehind = false;
            _pacer = new Thread(() => PaceLoop(fps)) { IsBackground = true, Name = "recmode-pacer" };
            _pacer.Start();

            // Audio pump runs on its own thread: ffmpeg opens the audio pipe only after it has probed the
            // video stream (which needs frames flowing), so we start video pacing first, then wait + pump.
            if (_mixer is not null && _session.AudioPipe is { } audioPipe)
            {
                StartAudioPumpThread(audioPipe);
            }

            Log.Information("Recording started: {Enc} {W}x{H}@{Fps} safe={Safe} audio={Audio} -> {Path}",
                encoder.FfmpegId, dstW, dstH, fps, _safeRemux, audioEnabled, _finalPath);
            return true;
        }
        catch (Exception ex)
        {
            _errors.Block("record.start-failed", "Couldn't start the recording.", "See the log for details.", ex);
            SafeTeardown();
            return false;
        }
    }

    /// <summary>§3.6 pre-flight checks, run before anything is actually started. Returns null (having already
    /// reported the specific BlockingError) on the first hard failure; warnings (disk space/speed, battery)
    /// never block and are reported inline.</summary>
    private PreflightResult? RunPreflight(CaptureTarget target, EncoderInfo encoder, MediaContainer container)
    {
        if (!MediaCompatibility.IsVideoCompatible(encoder.Codec, container))
        {
            _errors.Block("record.codec-container", MediaCompatibility.IncompatibilityReason(encoder.Codec, container),
                "Pick a compatible container or encoder in Settings.");
            return null;
        }

        FfmpegResolution ff = _ffmpeg.Resolve();
        if (!ff.IsAvailable || ff.FfmpegPath is null)
        {
            _errors.Block("record.no-ffmpeg", "Recording needs ffmpeg, which wasn't found.",
                ff.Error?.Suggestion ?? "Reinstall RecMode or set a custom ffmpeg path in Settings.");
            return null;
        }

        string outputDir = _settings.Current.OutputFolder ?? _paths.RecordingsDirectory;
        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Block("record.output-unwritable", "The output folder can't be written to.",
                "Choose a different folder in Settings.", ex);
            return null;
        }

        WarnIfLowDiskSpace(outputDir);
        WarnIfSlowDisk(outputDir);
        WarnIfOnBattery();

        if (!CaptureCapabilities.TryGetSourceSize(target, out int srcW, out int srcH))
        {
            _errors.Block("record.source-unavailable", "The selected source couldn't be captured.",
                "Pick a different display or window.");
            return null;
        }

        return new PreflightResult(outputDir, ff.FfmpegPath, srcW, srcH);
    }

    /// <summary>§3.6: warns under 2 GB free on the output volume. Also stashes the drive root for the
    /// mid-recording disk-critical guard (<see cref="IsDiskCriticallyLow"/>).</summary>
    private void WarnIfLowDiskSpace(string outputDir)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(outputDir));
            _outputRoot = root;
            if (root is not null)
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace < 2L * 1024 * 1024 * 1024)
                {
                    _errors.Warn("record.low-disk",
                        $"Low disk space ({drive.AvailableFreeSpace / (1024 * 1024 * 1024)} GB free).",
                        "The recording may stop early if the disk fills.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Free-space check is best-effort.
        }
    }

    /// <summary>§3.6 disk-speed signal: plenty of free space doesn't mean fast enough — catches network
    /// shares / old flash drives the free-space check alone would miss.</summary>
    private void WarnIfSlowDisk(string outputDir)
    {
        double diskMBps = _diskSpeed.MeasureWriteSpeedMBps(outputDir);
        Log.Debug("Disk-speed probe: {Mbps:F1} MB/s for {Dir}", diskMBps, outputDir);
        if (RecordingHealth.IsDiskTooSlow(diskMBps))
        {
            _errors.Warn("record.slow-disk",
                $"The output folder's drive looks slow (~{diskMBps:F1} MB/s).",
                "Recording may stutter or drop frames. Try a faster drive if this happens.");
        }
    }

    /// <summary>§3.6 / Phase 9: recording is power-hungry — nudge laptop users to plug in.</summary>
    private void WarnIfOnBattery()
    {
        if (_power.IsOnBattery)
        {
            string pct = _power.BatteryPercent is int b ? $" ({b}% left)" : "";
            _errors.Warn("record.on-battery", $"You're recording on battery power{pct}.",
                "Recording is power-hungry — plug in for long sessions.");
        }
    }

    /// <summary>Starts the webcam picture-in-picture overlay, if forced by a test seam or enabled in
    /// settings. Best-effort: a missing/busy camera warns but never blocks the recording. Must run after
    /// <see cref="_capture"/> has started, so the very first frame already carries the overlay.</summary>
    private void SetupWebcamOverlay(int dstW, int dstH)
    {
        if (_testForcedWebcamSource is { } forcedSource)
        {
            (int fx, int fy, int fw, int fh) = WebcamOverlayLayout.ComputeRect(
                dstW, dstH, _settings.Current.WebcamSizePercent, _settings.Current.WebcamPosition);
            _capture!.SetWebcamOverlay(forcedSource, new RegionRect(fx, fy, fw, fh));
        }
        else if (_settings.Current.WebcamEnabled && !string.IsNullOrEmpty(_settings.Current.WebcamDeviceId))
        {
            try
            {
                var webcam = new WebcamCaptureSource();
                webcam.StartAsync(_settings.Current.WebcamDeviceId).GetAwaiter().GetResult();
                _webcamCapture = webcam;
                (int wx, int wy, int ww, int wh) = WebcamOverlayLayout.ComputeRect(
                    dstW, dstH, _settings.Current.WebcamSizePercent, _settings.Current.WebcamPosition);
                _capture!.SetWebcamOverlay(_webcamCapture, new RegionRect(wx, wy, ww, wh));
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or COMException)
            {
                _webcamCapture = null;
                _errors.Warn("record.webcam-unavailable", "The webcam overlay couldn't be started.",
                    "Recording will continue without the picture-in-picture.");
            }
        }
    }

    /// <summary>Builds the <see cref="FfmpegJob"/> for the first segment and snapshots everything the rest
    /// of the recording (safe-remux, auto-split, library metadata) needs — as instance-field side effects,
    /// same as the inline code this was extracted from.</summary>
    private (FfmpegJob Job, bool AudioEnabled) PrepareSession(CaptureTarget target, EncoderInfo encoder,
        MediaContainer container, string outputDir, string ffmpegPath, int dstW, int dstH, int fps, int quality)
    {
        // Safe recording (§3): capture to MKV (crash-safe) then remux to MP4 (-c copy) on stop.
        _safeRemux = _settings.Current.SafeRecording && container is MediaContainer.Mp4 or MediaContainer.Mov;
        MediaContainer actualContainer = _safeRemux ? MediaContainer.Mkv : container;

        string sourceLabel = target.Kind switch
        {
            CaptureKind.Window => "Window",
            CaptureKind.Region => "Region",
            _ => "Display",
        };
        string fileName = FilenameBuilder.BuildFileName(
            _settings.Current.FilenamePattern, DateTimeOffset.Now, sourceLabel, encoder.Codec.ToString(),
            ContainerExtension(container));
        _finalPath = FilenameBuilder.BuildUniquePath(outputDir, fileName);
        _recordingPath = _safeRemux
            ? Path.Combine(outputDir, Path.GetFileNameWithoutExtension(_finalPath) + ".recording.mkv")
            : _finalPath;
        _ffmpegPath = ffmpegPath;

        // Auto-split bookkeeping: remember enough to rebuild subsequent segment file names/paths.
        _outputDir = outputDir;
        _baseFileName = fileName;
        _segmentIndex = 1;
        _autoSplitEnabled = _settings.Current.AutoSplitEnabled;
        _autoSplitThresholdBytes = Math.Max(100, _settings.Current.AutoSplitSizeMb) * 1024L * 1024L;
        _downgradeAttempted = false;
        _testForceDowngrade = false;

        // Snapshot metadata for the library index (written on successful finalize).
        _metaSource = sourceLabel;
        _metaCodec = encoder.Codec.ToString();
        _metaContainer = container.ToString();
        _metaWidth = dstW;
        _metaHeight = dstH;
        _metaFps = fps;
        _metaQuality = quality;
        _metaSystemAudioEnabled = _settings.Current.SystemAudioEnabled;
        _metaMicEnabled = _settings.Current.MicrophoneEnabled;

        bool audioEnabled = _settings.Current.SystemAudioEnabled || _settings.Current.MicrophoneEnabled;
        string? audioPipeName = audioEnabled ? $"recmode_aud_{Environment.ProcessId}_{Environment.TickCount}" : null;

        var job = new FfmpegJob
        {
            Encoder = encoder,
            Container = actualContainer,
            Width = dstW,
            Height = dstH,
            FrameRate = fps,
            Quality = quality,
            PipeName = $"recmode_vid_{Environment.ProcessId}_{Environment.TickCount}",
            OutputPath = _recordingPath,
            AudioPipeName = audioPipeName,
            AudioCodec = _settings.Current.AudioCodec,
            AudioBitrateKbps = _settings.Current.AudioBitrateKbps,
            CpuThreadCap = _settings.Current.CpuThreadCap,
            BelowNormalPriority = _settings.Current.BelowNormalEncoderPriority,
            Effort = _settings.Current.Effort,
        };

        return (job, audioEnabled);
    }

    /// <summary>Starts the recording's audio mixer (system loopback + mic, per current settings).</summary>
    private void StartAudioMixer()
    {
        bool captureSystem = _settings.Current.SystemAudioEnabled;

        // Per-app audio targeting (plan §7): settings persist the target by process NAME (PIDs don't
        // survive relaunches), so resolve it to a live PID at record-start time. If the targeted app
        // isn't running right now, fail closed (no system audio) rather than silently falling back to
        // full-system capture, which the user didn't ask for — same philosophy as AudioMixer.Start's
        // own activation-failure fallback.
        int? perAppPid = null;
        string? targetName = _settings.Current.PerAppAudioProcessName;
        if (captureSystem && !string.IsNullOrEmpty(targetName))
        {
            AudioProcessTarget? target = CaptureCapabilities.EnumerateAudioProcesses()
                .FirstOrDefault(p => string.Equals(p.ProcessName, targetName, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                perAppPid = target.ProcessId;
            }
            else
            {
                captureSystem = false;
            }
        }

        _mixer = _mixerFactory();
        _mixer.Start(captureSystem, _settings.Current.MicrophoneEnabled, perAppPid);
        _mixer.SystemGain = _settings.Current.SystemVolume / 100f;
        _mixer.MicGain = _settings.Current.MicVolume / 100f;
    }

    /// <summary>Stops the current recording and finalizes the file.</summary>
    public void Stop()
    {
        if (!_stateMachine.IsActive)
        {
            return;
        }

        _stopRequested = true;
        _pacer?.Join(3000);

        if (!TryClaimFinalize())
        {
            // The pacer thread's own fatal-error path (HandleFatalPipeBreak) already claimed finalize —
            // e.g. the encoder died right as Stop() was called. It already drove the state machine to Idle
            // and raised Finished; nothing more to do here.
            return;
        }

        if (_stateMachine.State is RecordingState.Recording or RecordingState.Paused)
        {
            _stateMachine.Stop();
        }

        RecordingResult result = Finalize();
        if (_stateMachine.State == RecordingState.Finalizing)
        {
            _stateMachine.CompleteFinalization();
        }

        Finished?.Invoke(result);
    }

    /// <summary>Atomically claims the single finalize attempt for the current recording — guards against
    /// Stop() (UI thread) and the pacer thread's fatal-error path both trying to finalize/transition state
    /// concurrently, which could otherwise double-finalize, throw from a state-machine guard, or fire
    /// Finished twice. Reset per-recording in Start().</summary>
    private bool TryClaimFinalize()
    {
        lock (_finalizeLock)
        {
            if (_finalizeStarted)
            {
                return false;
            }

            _finalizeStarted = true;
            return true;
        }
    }

    /// <summary>Pauses the recording — the pacer stops writing; output has no gap for the paused span (§3.7).</summary>
    public void Pause()
    {
        if (_stateMachine.State == RecordingState.Recording)
        {
            _stateMachine.Pause();
            RaiseProgress();
        }
    }

    /// <summary>Resumes a paused recording.</summary>
    public void Resume()
    {
        if (_stateMachine.State == RecordingState.Paused)
        {
            _stateMachine.Resume();
            RaiseProgress();
        }
    }

    public bool IsPaused => _stateMachine.State == RecordingState.Paused;

    /// <summary>Applies per-source gains (0..1) to the live recording mixer, so volume changes take effect mid-recording.</summary>
    public void SetAudioGains(float systemGain, float micGain)
    {
        if (_mixer is { } mixer)
        {
            mixer.SystemGain = systemGain;
            mixer.MicGain = micGain;
        }
    }

    /// <summary>Starts the audio-pump thread for the given pipe — shared by <see cref="Start"/> and
    /// <see cref="RotateSegment"/> (auto-split / hw→sw downgrade), which each open a fresh audio pipe per
    /// segment. Runs on a dedicated background <see cref="Thread"/> (not a <see cref="System.Threading.Tasks.Task"/>):
    /// anything unhandled here would otherwise be a genuinely unhandled thread exception, which terminates
    /// the whole process immediately — so the catch is deliberately broad. The video pacer is the source of
    /// truth for stopping the recording; losing audio mid-recording is degraded, not fatal, so this just logs
    /// and stops pumping.</summary>
    private void StartAudioPumpThread(NamedPipeServerStream audioPipe)
    {
        _audioStop = new CancellationTokenSource();
        CancellationTokenSource stopSource = _audioStop;
        _audioThread = new Thread(() =>
        {
            try
            {
                audioPipe.WaitForConnection();
                _mixer!.PumpUntil(audioPipe, () => _stateMachine.Elapsed, stopSource.Token);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Audio pump ended");
            }
        }) { IsBackground = true, Name = "recmode-audio" };
        _audioThread.Start();
    }

    private void PaceLoop(int fps)
    {
        byte[] frame = new byte[_capture!.Nv12ByteSize];
        int lumaLength = _capture.OutputWidth * _capture.OutputHeight;
        long framesWritten = 0;
        long lastReport = Stopwatch.GetTimestamp();
        long blackSince = 0;
        bool blackWarned = false;
        long behindSince = 0;
        bool degradeWarned = false;
        long lastDiskCheck = Stopwatch.GetTimestamp();
        long lastSplitCheck = Stopwatch.GetTimestamp();

        _ = timeBeginPeriod(1);
        try
        {
            while (!_stopRequested)
            {
                if (_stateMachine.State == RecordingState.Paused)
                {
                    Thread.Sleep(4);
                    continue;
                }

                // CFR output paced by ACTIVE elapsed time (excludes paused spans). ffmpeg assigns PTS by
                // frame index at -r fps, so writing exactly Elapsed·fps frames yields gapless pause/resume
                // and duplicates the latest frame to fill gaps (the §3.3 CFR policy).
                long targetFrames = (long)(_stateMachine.Elapsed.TotalSeconds * fps);
                if (framesWritten >= targetFrames)
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (!_capture.TryGetLatestFrame(frame))
                {
                    Thread.Sleep(1);
                    continue; // no first frame yet
                }

                _session!.WriteFrame(frame, frame.Length);
                framesWritten++;

                long now = Stopwatch.GetTimestamp();

                // Black-frame watchdog (§3.6): exclusive-fullscreen games / DRM windows capture as black.
                if (!blackWarned && (framesWritten & 15) == 0)
                {
                    if (BlackFrameDetector.IsLikelyBlack(frame, lumaLength))
                    {
                        if (blackSince == 0)
                        {
                            blackSince = now;
                        }
                        else if (now - blackSince > 3 * Stopwatch.Frequency)
                        {
                            blackWarned = true;
                            _errors.Warn("record.black-frames",
                                "The recording looks black. Exclusive-fullscreen games and DRM-protected windows can't be captured.",
                                "Switch the game to borderless/windowed mode.");
                        }
                    }
                    else
                    {
                        blackSince = 0;
                    }
                }

                // Health (§3.6 recording health): if the encoder can't keep up, WriteFrame back-pressures and
                // we fall > 1 s behind real time. Sustained for 3 s → Degraded (see RecordingHealth).
                double elapsedS = _stateMachine.Elapsed.TotalSeconds;
                if (RecordingHealth.IsBehindRealtime(elapsedS, framesWritten, fps))
                {
                    if (behindSince == 0)
                    {
                        behindSince = now;
                    }
                    else if (now - behindSince > 3 * Stopwatch.Frequency)
                    {
                        _encoderBehind = true;
                        if (!degradeWarned)
                        {
                            degradeWarned = true;
                            _errors.Degrade("record.encoder-slow",
                                "The encoder can't keep up — the recording may run slow.",
                                "Try a lower resolution or frame rate, or a hardware encoder.");
                        }

                        // Mid-stream hw→sw fallback: still behind well past the Degraded threshold → switch
                        // the hardware encoder out for a software one on a fresh segment (once per recording).
                        double behindSeconds = (now - behindSince) / (double)Stopwatch.Frequency;
                        if (RecordingHealth.ShouldDowngradeToSoftware(behindSeconds, _activeEncoder?.IsHardware ?? false))
                        {
                            AttemptDowngrade();
                            behindSince = 0; // fresh grace period for the new encoder
                        }
                    }
                }
                else if (RecordingHealth.FramesBehind(elapsedS, framesWritten, fps) <= fps / 2)
                {
                    behindSince = 0;
                    _encoderBehind = false;
                }

                // Test-only seam (--selftest-downgrade): force the same rotation the health check would trigger.
                if (_testForceDowngrade)
                {
                    _testForceDowngrade = false;
                    AttemptDowngrade();
                }

                // Mid-recording disk guard (§3.6): stop gracefully before a full disk corrupts the finish.
                if (now - lastDiskCheck >= 2 * Stopwatch.Frequency) // every ~2 s
                {
                    lastDiskCheck = now;
                    if (IsDiskCriticallyLow())
                    {
                        _errors.Warn("record.disk-critical",
                            "Stopping — the disk is nearly full.",
                            "Free up space or choose another output folder before recording again.");
                        System.Threading.Tasks.Task.Run(Stop); // Stop() joins this thread, so never call it inline
                        return;
                    }
                }

                // Auto-split (§3.3): roll to a new segment file once the current one crosses the threshold.
                if (_autoSplitEnabled && now - lastSplitCheck >= Stopwatch.Frequency) // ~1 Hz
                {
                    lastSplitCheck = now;
                    if (TryGetSegmentSize(out long segSize) && segSize >= _autoSplitThresholdBytes)
                    {
                        RotateSegment();
                    }
                }

                if (now - lastReport >= Stopwatch.Frequency / 4) // ≤ 4 Hz
                {
                    lastReport = now;
                    RaiseProgress();
                }
            }
        }
        catch (EncoderPipeBrokenException ex)
        {
            HandleFatalPipeBreak(ex, "The encoder stopped unexpectedly; the recording was ended.",
                "A partial file may be recoverable if you recorded to MKV.");
        }
        catch (Exception ex)
        {
            // This loop runs on a dedicated background Thread (not a Task) — an exception that escapes it
            // would otherwise be a genuinely unhandled thread exception, which terminates the whole process
            // immediately (unlike Task exceptions, which are non-fatal by default). Treat anything
            // unexpected here (a race with capture/session teardown, etc.) the same as a pipe break: end
            // the recording gracefully instead of crashing the app.
            HandleFatalPipeBreak(ex, "The recording stopped unexpectedly.",
                "See the log for details. A partial file may be recoverable if you recorded to MKV.");
        }
        finally
        {
            _ = timeEndPeriod(1);
        }
    }

    private void HandleFatalPipeBreak(Exception ex, string message, string suggestion)
    {
        Log.Error(ex, "Recording pacer loop failed. ffmpeg stderr:\n{Stderr}", _session?.StandardError);
        _errors.Fatal("record.encoder-died", message, suggestion, ex);

        if (!TryClaimFinalize())
        {
            // Stop() (UI thread) already claimed finalize concurrently — it will drive the state machine
            // and raise Finished; nothing more to do here.
            return;
        }

        // Drive the machine to a clean Idle and report whatever the session managed to finalize.
        try
        {
            if (_stateMachine.State is RecordingState.Recording or RecordingState.Paused)
            {
                _stateMachine.Stop();
            }

            RecordingResult result = Finalize();
            if (_stateMachine.State == RecordingState.Finalizing)
            {
                _stateMachine.CompleteFinalization();
            }

            Finished?.Invoke(result with { Success = false });
        }
        catch (Exception teardownEx)
        {
            Log.Error(teardownEx, "Teardown after pacer loop failure failed");
        }
    }

    private RecordingResult Finalize()
    {
        // Stop the audio pump before the session closes the pipes.
        _audioStop?.Cancel();
        _audioThread?.Join(3000);

        string stderr = _session?.StandardError ?? "";
        RecordingResult result = _session?.StopAndFinalize(TimeSpan.FromSeconds(20))
            ?? new RecordingResult(false, -1, "", 0);

        if (!result.Success && stderr.Length > 0)
        {
            Log.Warning("ffmpeg stderr:\n{Stderr}", stderr);
        }

        _capture?.Stop();
        _webcamCapture?.Stop();
        _session?.Dispose();
        _capture?.Dispose();
        _mixer?.Dispose();
        _audioStop?.Dispose();
        _session = null;
        _capture = null;
        _webcamCapture = null;
        _mixer = null;
        _audioThread = null;
        _audioStop = null;

        // Safe recording: remux the crash-safe MKV to MP4 without re-encoding.
        if (result.Success && _safeRemux)
        {
            if (Remux(_recordingPath, _finalPath))
            {
                TryDelete(_recordingPath);
                result = result with { OutputPath = _finalPath };
            }
            else
            {
                _errors.Warn("record.remux-failed",
                    "Saved as MKV — converting to MP4 failed, but your recording is safe.",
                    "The .recording.mkv file is playable and can be converted manually.");
                result = result with { OutputPath = _recordingPath };
            }
        }

        if (result.Success && result.OutputPath.Length > 0)
        {
            double duration = _metaFps > 0 ? (double)result.FramesWritten / _metaFps : 0;
            _libraryIndex.Add(new RecMode.Core.Library.LibraryIndexEntry(
                Path.GetFileName(result.OutputPath), _metaSource, _metaCodec, _metaContainer,
                _metaWidth, _metaHeight, _metaFps, duration, DateTimeOffset.Now,
                _metaQuality, _metaSystemAudioEnabled, _metaMicEnabled));
        }

        Log.Information("Recording finalized: success={Success} frames={Frames} -> {Path}",
            result.Success, result.FramesWritten, result.OutputPath);
        return result;
    }

    /// <summary>Reads the live size of the segment currently being written, best-effort.</summary>
    private bool TryGetSegmentSize(out long size)
    {
        size = 0;
        try
        {
            if (_recordingPath.Length > 0 && File.Exists(_recordingPath))
            {
                size = new FileInfo(_recordingPath).Length;
                return true;
            }
        }
        catch (IOException)
        {
            // File momentarily locked; try again next tick.
        }

        return false;
    }

    private (string recordingPath, string finalPath) BuildSegmentPaths(int index)
    {
        string segFileName = FilenameBuilder.SegmentFileName(_baseFileName, index);
        string finalPath = FilenameBuilder.BuildUniquePath(_outputDir, segFileName);
        string recordingPath = _safeRemux
            ? Path.Combine(_outputDir, Path.GetFileNameWithoutExtension(finalPath) + ".recording.mkv")
            : finalPath;
        return (recordingPath, finalPath);
    }

    /// <summary>
    /// Closes out the current segment file (finalize + safe-remux + library-index entry, same as a normal
    /// Stop) and immediately opens a new ffmpeg session for the next segment — capture, audio mixer, and the
    /// state machine all keep running uninterrupted. Runs on the pacer thread; a rotation briefly pauses
    /// frame writes but the pacer's Elapsed-driven catch-up (§3.3 CFR policy) absorbs the gap.
    /// </summary>
    /// <param name="forcedChain">
    /// When set, the next segment tries only these encoders instead of the original fallback chain — used by
    /// the mid-stream hw→sw Degraded downgrade to force a software encoder. The forced chain also becomes the
    /// chain for any later rotation (a later auto-split split keeps using the downgraded encoder).
    /// </param>
    private void RotateSegment(List<EncoderInfo>? forcedChain = null)
    {
        _audioStop?.Cancel();
        _audioThread?.Join(3000);
        _audioThread = null;

        string prevRecordingPath = _recordingPath;
        string prevFinalPath = _finalPath;
        RecordingResult segResult = _session?.StopAndFinalize(TimeSpan.FromSeconds(20)) ?? new RecordingResult(false, -1, "", 0);
        _session?.Dispose();
        _session = null;

        if (segResult.Success && _safeRemux)
        {
            if (Remux(prevRecordingPath, prevFinalPath))
            {
                TryDelete(prevRecordingPath);
            }
            else
            {
                prevFinalPath = prevRecordingPath;
            }
        }

        if (segResult.Success && prevFinalPath.Length > 0)
        {
            double duration = _targetFps > 0 ? (double)segResult.FramesWritten / _targetFps : 0;
            _libraryIndex.Add(new RecMode.Core.Library.LibraryIndexEntry(
                Path.GetFileName(prevFinalPath), _metaSource, _metaCodec, _metaContainer,
                _metaWidth, _metaHeight, _metaFps, duration, DateTimeOffset.Now,
                _metaQuality, _metaSystemAudioEnabled, _metaMicEnabled));
        }

        _segmentIndex++;
        (_recordingPath, _finalPath) = BuildSegmentPaths(_segmentIndex);

        var job = _jobTemplate! with
        {
            OutputPath = _recordingPath,
            PipeName = $"recmode_vid_{Environment.ProcessId}_{Environment.TickCount}",
            AudioPipeName = _jobTemplate.AudioPipeName is null ? null : $"recmode_aud_{Environment.ProcessId}_{Environment.TickCount}",
        };

        List<EncoderInfo> chain = forcedChain ?? _encoderChain!;
        _session = TryStartAnyEncoder(chain, job, _capture!.Nv12ByteSize);
        if (_session is null)
        {
            _errors.Fatal("record.split-failed", "Couldn't start the next recording segment; the recording was stopped.",
                "The previous segments are safe on disk.");
            System.Threading.Tasks.Task.Run(Stop); // Stop() joins the pacer thread, so never call it inline
            return;
        }

        if (forcedChain is not null)
        {
            _encoderChain = forcedChain; // keep any later rotation (e.g. auto-split) on the downgraded encoder
        }

        if (_mixer is not null && _session.AudioPipe is { } audioPipe)
        {
            StartAudioPumpThread(audioPipe);
        }

        Log.Information("Segment rotation: started segment {Index} (encoder={Enc}) -> {Path}",
            _segmentIndex, _activeEncoder?.FfmpegId, _finalPath);
    }

    /// <summary>
    /// Mid-stream hw→sw Degraded fallback (§3.6): rotates to a new segment encoded in software, once per
    /// recording. Called from the pacer thread only — either by the sustained-behind health check or the
    /// <see cref="_testForceDowngrade"/> test seam (<c>--selftest-downgrade</c>), which exercises the exact
    /// same rotation path without needing a genuinely overloaded encoder.
    /// </summary>
    private void AttemptDowngrade()
    {
        if (_downgradeAttempted || _activeEncoder is not { IsHardware: true } activeEncoder)
        {
            return;
        }

        _downgradeAttempted = true;
        List<EncoderInfo> swChain = _fallbackChain.BuildSoftwareOnly(activeEncoder);
        if (swChain.Count == 0)
        {
            return; // no software encoder available for this codec — nothing to fall back to
        }

        _errors.Warn("record.encoder-downgrade",
            "Switching to software encoding — the hardware encoder couldn't keep up.",
            "This uses more CPU but should stay in sync with real time.");
        RotateSegment(swChain);
    }

    /// <summary>Test-only seam (mirrors the temporary --selftest-* hooks): forces the hw→sw downgrade path
    /// deterministically instead of waiting for a genuine sustained encoder stall.</summary>
    internal void TestForceDowngrade() => _testForceDowngrade = true;

    private FfmpegRecordingSession? TryStartAnyEncoder(List<EncoderInfo> chain, FfmpegJob template, int frameBytes)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            EncoderInfo enc = chain[i];
            var session = new FfmpegRecordingSession(_ffmpegPath!);
            try
            {
                session.Start(template with { Encoder = enc }, frameBytes);
                if (i > 0)
                {
                    _errors.Warn("record.encoder-fallback",
                        $"Using {enc.DisplayName} — the selected encoder wouldn't start.");
                }
                _activeEncoder = enc;
                return session;
            }
            catch (Exception ex) when (ex is EncoderStartException or InvalidOperationException)
            {
                Log.Warning(ex, "Encoder {Enc} failed to start; trying next", enc.FfmpegId);
                session.Dispose();
            }
        }

        return null;
    }

    private bool Remux(string mkvPath, string mp4Path) =>
        _ffmpegPath is not null && RecMode.Encoding.Ffmpeg.Remuxer.RemuxToMp4(_ffmpegPath, mkvPath, mp4Path);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave the file; it's harmless.
        }
    }

    private bool IsDiskCriticallyLow()
    {
        if (_outputRoot is null)
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(_outputRoot);
            return drive.IsReady && RecordingHealth.IsDiskCritical(drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false; // best-effort — never stop a recording over a failed probe
        }
    }

    private long _lastSizeBytes;
    private long _lastSizeTicks;
    private int _targetFps;
    private volatile bool _encoderBehind; // health: the encoder can't keep up with real time
    private string? _outputRoot;          // drive root for the mid-recording disk-space guard

    private void RaiseProgress()
    {
        double fps = _capture is null || _stateMachine.Elapsed.TotalSeconds < 0.1
            ? 0
            : _session!.FramesWritten / _stateMachine.Elapsed.TotalSeconds;

        long size = 0;
        double mbps = 0;
        try
        {
            if (_recordingPath.Length > 0 && File.Exists(_recordingPath))
            {
                size = new FileInfo(_recordingPath).Length;
                long now = Stopwatch.GetTimestamp();
                if (_lastSizeTicks != 0)
                {
                    double dt = (now - _lastSizeTicks) / (double)Stopwatch.Frequency;
                    if (dt > 0)
                    {
                        mbps = (size - _lastSizeBytes) * 8 / dt / 1_000_000.0;
                    }
                }
                _lastSizeBytes = size;
                _lastSizeTicks = now;
            }
        }
        catch (IOException)
        {
            // File momentarily locked; skip this sample.
        }

        ProgressChanged?.Invoke(new RecordingProgress(
            _stateMachine.State, _stateMachine.Elapsed, fps, _session?.FramesWritten ?? 0, mbps, size,
            IsHealthy: !_encoderBehind));
    }

    private void SafeTeardown()
    {
        try { _audioStop?.Cancel(); } catch (Exception) { }
        try { _audioThread?.Join(1000); } catch (Exception) { }
        try { _session?.Dispose(); } catch (Exception) { }
        try { _capture?.Dispose(); } catch (Exception) { }
        try { _webcamCapture?.Stop(); } catch (Exception) { }
        try { _mixer?.Dispose(); } catch (Exception) { }
        try { _audioStop?.Dispose(); } catch (Exception) { }
        _session = null;
        _capture = null;
        _webcamCapture = null;
        _mixer = null;
        _audioThread = null;
        _audioStop = null;
    }

    private static string ContainerExtension(MediaContainer c) => c switch
    {
        MediaContainer.Mp4 => "mp4",
        MediaContainer.Mkv => "mkv",
        MediaContainer.Mov => "mov",
        MediaContainer.WebM => "webm",
        _ => "mp4",
    };

    public void Dispose() => SafeTeardown();
}
