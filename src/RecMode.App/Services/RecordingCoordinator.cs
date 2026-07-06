using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using RecMode.Audio;
using RecMode.Capture;
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
    private FfmpegRecordingSession? _session;
    private Thread? _pacer;
    private volatile bool _stopRequested;

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
    private readonly IPowerStatus _power;
    private readonly RecMode.Core.Library.ILibraryIndex _libraryIndex;

    // Metadata snapshot for the library index (captured at Start, written on successful finalize).
    private string _metaSource = "", _metaCodec = "", _metaContainer = "";
    private int _metaWidth, _metaHeight, _metaFps;

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
        RecMode.Core.Library.ILibraryIndex libraryIndex)
    {
        _captureFactory = captureFactory;
        _ffmpeg = ffmpeg;
        _paths = paths;
        _settings = settings;
        _errors = errors;
        _stateMachine = stateMachine;
        _encoderProbe = encoderProbe;
        _power = power;
        _libraryIndex = libraryIndex;
        _mixerFactory = mixerFactory;
    }

    public RecordingState State => _stateMachine.State;
    public bool IsRecording => _stateMachine.IsActive;

    /// <summary>Throttled progress (≤ 4 Hz). Raised on the pacing thread — the VM marshals to the dispatcher.</summary>
    public event Action<RecordingProgress>? ProgressChanged;

    /// <summary>Raised on stop/finalize with the outcome. Also raised (Success=false) on a fatal mid-recording failure.</summary>
    public event Action<RecordingResult>? Finished;

    /// <summary>Starts a recording. Returns false (with a reported BlockingError) if pre-flight fails.</summary>
    public bool Start(CaptureTarget target, EncoderInfo encoder, MediaContainer container, int fps, int quality)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(encoder);
        if (_stateMachine.IsActive)
        {
            return false;
        }

        // --- Pre-flight (§3.6) ---
        if (!MediaCompatibility.IsVideoCompatible(encoder.Codec, container))
        {
            _errors.Block("record.codec-container", MediaCompatibility.IncompatibilityReason(encoder.Codec, container),
                "Pick a compatible container or encoder in Settings.");
            return false;
        }

        FfmpegResolution ff = _ffmpeg.Resolve();
        if (!ff.IsAvailable || ff.FfmpegPath is null)
        {
            _errors.Block("record.no-ffmpeg", "Recording needs ffmpeg, which wasn't found.",
                ff.Error?.Suggestion ?? "Reinstall RecMode or set a custom ffmpeg path in Settings.");
            return false;
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
            return false;
        }

        // Disk-space pre-flight (§3.6): warn under 2 GB free on the output volume.
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

        // Battery pre-flight (§3.6 / Phase 9): recording is power-hungry — nudge laptop users to plug in.
        if (_power.IsOnBattery)
        {
            string pct = _power.BatteryPercent is int b ? $" ({b}% left)" : "";
            _errors.Warn("record.on-battery", $"You're recording on battery power{pct}.",
                "Recording is power-hungry — plug in for long sessions.");
        }

        if (!CaptureCapabilities.TryGetSourceSize(target, out int srcW, out int srcH))
        {
            _errors.Block("record.source-unavailable", "The selected source couldn't be captured.",
                "Pick a different display or window.");
            return false;
        }

        try
        {
            (int dstW, int dstH) = CaptureSizing.Resolve(srcW, srcH, encoder);

            _capture = _captureFactory();
            _capture.Start(target, dstW, dstH, _settings.Current.CaptureCursor);

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
            _ffmpegPath = ff.FfmpegPath;

            // Snapshot metadata for the library index (written on successful finalize).
            _metaSource = sourceLabel;
            _metaCodec = encoder.Codec.ToString();
            _metaContainer = container.ToString();
            _metaWidth = dstW;
            _metaHeight = dstH;
            _metaFps = fps;

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

            if (audioEnabled)
            {
                _mixer = _mixerFactory();
                _mixer.Start(_settings.Current.SystemAudioEnabled, _settings.Current.MicrophoneEnabled);
                _mixer.SystemGain = _settings.Current.SystemVolume / 100f;
                _mixer.MicGain = _settings.Current.MicVolume / 100f;
            }

            // Encoder fallback chain (§3.6): selected → same-codec other backend → any hw H.264 → libx264.
            _session = TryStartAnyEncoder(BuildFallbackChain(encoder), job, _capture.Nv12ByteSize);
            if (_session is null)
            {
                _errors.Block("record.no-encoder", "No video encoder could be started.",
                    "Try a different encoder in Settings.");
                SafeTeardown();
                return false;
            }

            _stateMachine.StartRecording();
            _stopRequested = false;
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
                _audioStop = new CancellationTokenSource();
                _audioThread = new Thread(() =>
                {
                    try
                    {
                        audioPipe.WaitForConnection();
                        _mixer.PumpUntil(audioPipe, () => _stateMachine.Elapsed, _audioStop.Token);
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        Log.Warning(ex, "Audio pump ended");
                    }
                }) { IsBackground = true, Name = "recmode-audio" };
                _audioThread.Start();
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

    /// <summary>Stops the current recording and finalizes the file.</summary>
    public void Stop()
    {
        if (!_stateMachine.IsActive)
        {
            return;
        }

        _stopRequested = true;
        _pacer?.Join(3000);

        _stateMachine.Stop();
        RecordingResult result = Finalize();
        _stateMachine.CompleteFinalization();

        Finished?.Invoke(result);
    }

    /// <summary>Pauses the recording — the pacer stops writing; output has no gap for the paused span (§3.7).</summary>
    public void Pause()
    {
        if (_stateMachine.State is RecordingState.Recording or RecordingState.Degraded)
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

                try
                {
                    _session!.WriteFrame(frame, frame.Length);
                }
                catch (EncoderPipeBrokenException ex)
                {
                    HandleFatalPipeBreak(ex);
                    return;
                }
                framesWritten++;

                long now = Stopwatch.GetTimestamp();

                // Black-frame watchdog (§3.6): exclusive-fullscreen games / DRM windows capture as black.
                if (!blackWarned && (framesWritten & 15) == 0)
                {
                    if (IsLikelyBlack(frame, lumaLength))
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
                    }
                }
                else if (RecordingHealth.FramesBehind(elapsedS, framesWritten, fps) <= fps / 2)
                {
                    behindSince = 0;
                    _encoderBehind = false;
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

                if (now - lastReport >= Stopwatch.Frequency / 4) // ≤ 4 Hz
                {
                    lastReport = now;
                    RaiseProgress();
                }
            }
        }
        finally
        {
            _ = timeEndPeriod(1);
        }
    }

    private void HandleFatalPipeBreak(EncoderPipeBrokenException ex)
    {
        Log.Error(ex, "Encoder pipe broke mid-recording. ffmpeg stderr:\n{Stderr}", _session?.StandardError);
        _errors.Fatal("record.encoder-died", "The encoder stopped unexpectedly; the recording was ended.",
            "A partial file may be recoverable if you recorded to MKV.", ex);

        // Drive the machine to a clean Idle and report whatever the session managed to finalize.
        try
        {
            if (_stateMachine.State is RecordingState.Recording or RecordingState.Paused or RecordingState.Degraded)
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
            Log.Error(teardownEx, "Teardown after pipe break failed");
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
        _session?.Dispose();
        _capture?.Dispose();
        _mixer?.Dispose();
        _audioStop?.Dispose();
        _session = null;
        _capture = null;
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
                _metaWidth, _metaHeight, _metaFps, duration, DateTimeOffset.Now));
        }

        Log.Information("Recording finalized: success={Success} frames={Frames} -> {Path}",
            result.Success, result.FramesWritten, result.OutputPath);
        return result;
    }

    private List<EncoderInfo> BuildFallbackChain(EncoderInfo selected)
    {
        IReadOnlyList<EncoderInfo> available = _encoderProbe.GetAvailableEncoders();
        var chain = new List<EncoderInfo> { selected };

        void Add(EncoderInfo? e)
        {
            if (e is not null && !chain.Exists(c => c.FfmpegId == e.FfmpegId))
            {
                chain.Add(e);
            }
        }

        foreach (EncoderInfo e in available.Where(x => x.Codec == selected.Codec))
        {
            Add(e); // same codec, other backends
        }
        foreach (EncoderInfo e in available.Where(x => x is { Codec: VideoCodec.H264, IsHardware: true }))
        {
            Add(e); // any hardware H.264
        }
        Add(available.FirstOrDefault(x => x.FfmpegId == "libx264")); // last-resort software

        return chain;
    }

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

    /// <summary>Cheap black-frame test: sample the NV12 luma plane; near-zero everywhere ≈ black.</summary>
    private static bool IsLikelyBlack(byte[] nv12, int lumaLength)
    {
        if (lumaLength <= 0)
        {
            return false;
        }

        const int samples = 256;
        int stepSize = Math.Max(1, lumaLength / samples);
        byte max = 0;
        for (int i = 0; i < lumaLength; i += stepSize)
        {
            if (nv12[i] > max)
            {
                max = nv12[i];
            }
        }

        return max < 20; // studio-black luma is ~16
    }

    private void SafeTeardown()
    {
        try { _audioStop?.Cancel(); } catch (Exception) { }
        try { _audioThread?.Join(1000); } catch (Exception) { }
        try { _session?.Dispose(); } catch (Exception) { }
        try { _capture?.Dispose(); } catch (Exception) { }
        try { _mixer?.Dispose(); } catch (Exception) { }
        _session = null;
        _capture = null;
        _mixer = null;
        _audioThread = null;
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
