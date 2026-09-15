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
    // Guards every read-then-invoke of _capture from outside the pacer loop (SetBrightness/SetZoomTarget/
    // SetBaseRect/CaptureSupportsZoom — called from the UI thread and, for auto-zoom, a threadpool timer)
    // against RetargetCapture/Finalize concurrently swapping or disposing it on the pacer thread. `_capture?.
    // SetBrightness(value)` alone reads the field once into a local before the null check, so it can't NRE —
    // but that local can still be a reference to an engine another thread disposes a moment later, between
    // the read and the call. Deliberately NOT held around the pacer loop's own per-frame use of _capture —
    // only around the occasional swap/dispose and the infrequent external setter calls, so this adds no
    // per-frame lock contention to the hot path.
    private readonly Lock _captureAccessLock = new();
    private WebcamCaptureSource? _webcamCapture;
    private FfmpegRecordingSession? _session;
    private Thread? _pacer;
    private volatile bool _stopRequested;

    // Guards Finalize() against running twice — Stop() (UI thread) and the pacer thread's own fatal-error
    // path (HandleFatalPipeBreak) can both reach a finalize attempt if the encoder dies right as the user
    // clicks Stop; only the first to claim the lock actually finalizes/transitions state/raises Finished.
    private readonly Lock _finalizeLock = new();
    private readonly ManualResetEventSlim _finalizationCompleted = new(initialState: true);
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

    // Set by RotateSegment right after a segment finalizes successfully, read (and cleared) by Finalize()
    // when _session is null. Without this, pressing Stop while a rotation's finalize+remux+library-write is
    // in flight (tens of seconds for a multi-GB segment) reported a fully successful recording as FAILED:
    // RotateSegment's own "_stopRequested" early-return leaves _session null by design (starting a fresh
    // encoder for a segment about to be abandoned would be pointless), but Finalize() then had no way to tell
    // "nothing to finalize because we just legitimately rotated it away" apart from "nothing to finalize
    // because startup never got this far" — both produced the same hardcoded RecordingResult(false, -1, "", 0).
    private RecordingResult? _lastRotatedSegmentResult;

    // Mid-stream hw→sw Degraded fallback (§3.6 / Phase 3 tail): the encoder actually in use for the current
    // segment, and whether a downgrade has already been attempted this recording (once per recording).
    private EncoderInfo? _activeEncoder;
    private bool _downgradeAttempted;
#if RECMODE_SELFTEST
    private volatile bool _testForceDowngrade; // test-only seam for --selftest-downgrade; see AttemptDowngrade

    // Test-only seam (--selftest-webcam): injects a synthetic frame source in place of a real
    // WebcamCaptureSource, so the GPU picture-in-picture compositing can be verified without camera hardware.
    private IWebcamFrameSource? _testForcedWebcamSource;
    internal void TestForceWebcamSource(IWebcamFrameSource source) => _testForcedWebcamSource = source;
#endif

    // Draw-on-screen annotation for Window-source recordings (see SetAnnotating): the target actually passed
    // to Start(), the fixed encoder output size, and a pending capture swap applied by the pacer thread only
    // (never mutated cross-thread — mirrors AttemptDowngrade's "self-mutation on the pacer thread" pattern).
    private CaptureTarget? _originalTarget;
    private int _dstW, _dstH;
    private volatile CaptureTarget? _pendingRetarget;

    // Follow-window-resize (Window source only): the window's on-screen size last seen, so PaceLoop can
    // detect a resize by polling and queue the same hot-swap SetAnnotating/SetClickHighlightActive/
    // SetKeystrokeVisualizerActive use. Pacer-thread-owned except for the initial value set in Start();
    // the three _*Active flags below are set from the UI thread by their respective Set* methods.
    /// <summary>Bound on the user-configurable A/V sync offset. Half a second each way is far beyond any
    /// plausible capture-pipeline mismatch (ITU-R BT.1359-1 puts even the *acceptability* limit around
    /// 90-185ms), and an unbounded value would prepend arbitrarily much silence to the recording.</summary>
    private const int MaxAudioSyncOffsetMs = 500;

    /// <summary>Pure, so it's directly testable without driving a real audio pump — see
    /// <c>RecordingCoordinatorTests</c>. Extracted rather than inlining <c>Math.Clamp</c> at the call site so
    /// a test can assert against the actual production bound, not reimplement the clamp itself.</summary>
    internal static int ClampAudioSyncOffsetMs(int raw) => Math.Clamp(raw, -MaxAudioSyncOffsetMs, MaxAudioSyncOffsetMs);

    private bool _isAnnotating;
    private bool _clickHighlightActive;
    private bool _keystrokeVisualizerActive;

    /// <summary>True while any feature that draws into its own separate top-level overlay window — draw-on-
    /// screen annotation, the click-highlight ripple, the keystroke visualizer — is active. All three need the
    /// identical Window→Region-proxy substitution (see <see cref="ApplyWindowOverlayProxyChange"/>): WGC's
    /// per-window capture only sees a Window source's own rendered content, never anything a different,
    /// independent window layers on top of it, so without this substitution any of these overlays would show
    /// live on screen but never appear in the actual recording.</summary>
    private bool NeedsWindowOverlayProxy => _isAnnotating || _clickHighlightActive || _keystrokeVisualizerActive;

    private int _lastWindowW, _lastWindowH;
    // Set alongside _pendingRetarget only by CheckWindowResize, which — unlike SetAnnotating's own use of
    // _pendingRetarget for the draw-on-screen Region-proxy swap — is only ever called from the pacer thread's
    // own loop, so this field (unlike _pendingRetarget itself) needs no cross-thread safety of its own.
    // Committed to _lastWindowW/_lastWindowH only once RetargetCapture actually succeeds — see the pacer
    // loop's consumption of _pendingRetarget for why.
    private (int W, int H)? _pendingResizeSize;

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
    public bool IsRecording => _stateMachine.IsBusy;

    /// <summary>Live levels from the recording's own audio mixer, or silence when not recording. Exposed so
    /// the Record screen's meters can read the levels this mixer is <em>already</em> computing on its capture
    /// callbacks, instead of opening a second, fully duplicate WASAPI graph alongside it (§3.9) — see
    /// <c>RecordViewModel.StartMetering</c>.</summary>
    public AudioLevel SystemAudioLevel => _mixer?.SystemLevel ?? AudioLevel.Silent;
    public AudioLevel MicAudioLevel => _mixer?.MicLevel ?? AudioLevel.Silent;

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
        if (_stateMachine.IsBusy)
        {
            return false;
        }

        try
        {
            if (RunPreflight(target, encoder, container) is not { } pf)
            {
                return false;
            }

            (int dstW, int dstH) = CaptureSizing.Resolve(pf.SourceWidth, pf.SourceHeight, encoder);
            _originalTarget = target;
            _dstW = dstW;
            _dstH = dstH;
            _pendingRetarget = null;
            _pendingResizeSize = null;
            _isAnnotating = false;
            _clickHighlightActive = false;
            _keystrokeVisualizerActive = false;
            _zoomMonitorCache = null;
            _zoomMonitorCacheHandle = 0;
            (_lastWindowW, _lastWindowH) = target.Kind == CaptureKind.Window &&
                CaptureCapabilities.TryGetWindowScreenRect(target.Handle, out RegionRect windowRect0)
                    ? (windowRect0.Width, windowRect0.Height)
                    : (0, 0);

            _capture = CreateCaptureEngine(target);
            _capture.Faulted += OnCaptureFaulted;
            _capture.Start(target, dstW, dstH, _settings.Current.CaptureCursor, fps);
            _capture.SetBrightness(_settings.Current.Brightness);

            // Smart auto-zoom needs the GPU VideoProcessor pipeline to crop with; the GDI software fallback
            // (Windows.Graphics.Capture unavailable) has no such mechanism. Rather than the setting silently
            // doing nothing, tell the user why up front — same "best-effort, warn don't block" spirit as the
            // webcam overlay below.
            if (_settings.Current.AutoZoomEnabled && !_capture.SupportsZoom)
            {
                _errors.Warn("record.autozoom-unsupported",
                    "Smart auto-zoom isn't available for this recording.",
                    "Screen capture fell back to a compatibility mode on this system, which can't apply the zoom effect. The rest of the recording is unaffected.");
            }

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
            // Filtered by job.Container (the container actually being muxed — MKV if safe-remux substituted
            // it, not necessarily the caller's original container) so a fallback candidate is never one the
            // container can't hold in the first place.
            _encoderChain = _fallbackChain.Build(encoder, job.Container);
            _jobTemplate = job;
            _session = TryStartAnyEncoder(_encoderChain, job, _capture.Nv12ByteSize);
            if (_session is null)
            {
                _errors.Block("record.no-encoder", "No video encoder could be started.",
                    "Try a different encoder in Settings.");
                SafeTeardown();
                return false;
            }

            // Reset the finalize latch BEFORE flipping IsBusy true (StartRecording(), next), not after. A
            // Stop() concurrent with this exact window (F9/tray/toolbar pressed a moment too early) reads
            // _stateMachine.IsBusy first — if that already reports true while _finalizeStarted still carries
            // the *previous* recording's claimed-and-completed value, TryClaimFinalize() fails, Stop() falls
            // into the "someone else already claimed it" branch, and _finalizationCompleted.Wait() returns
            // instantly because it's still Set from the prior recording — so Stop() does nothing and the
            // user's stop press is silently lost. Worse, for the very first recording of a session (where
            // _finalizeStarted starts false), that same race lets Stop() successfully claim finalize and tear
            // down _session/_capture/_mixer while this method is still assigning them further down, producing
            // a null-reference crash and a spurious Fatal toast. Resetting first closes both: by the time
            // IsBusy can observe true, a concurrent Stop() sees a freshly-armed latch for *this* attempt and
            // correctly finalizes whatever has been set up so far, instead of either silently no-op'ing or
            // colliding with still-in-flight initialization.
            lock (_finalizeLock)
            {
                _finalizeStarted = false;
                _finalizationCompleted.Reset();
                _stopRequested = false; // disarm before IsBusy can publish true, so a Stop in the earlier
                // window (between IsBusy and this line) can't claim this attempt's fresh latch then have us
                // overwrite the flag and launch a pacer over a torn-down _capture. See the stop-race note above.
            }
            _stateMachine.StartRecording();
            _lastSizeBytes = 0;
            _lastSizeTicks = 0;
            _currentSegmentStartedAt = TimeSpan.Zero;
            _targetFps = fps;
            _encoderBehind = false;
            _pacer = new Thread(() => PaceLoop(fps)) { IsBackground = true, Name = "recmode-pacer" };
            _pacer.Start();

            // Audio pump runs on its own thread: ffmpeg opens the audio pipe only after it has probed the
            // video stream (which needs frames flowing), so we start video pacing first, then wait + pump.
            if (_mixer is not null && _session.AudioPipe is { } audioPipe)
            {
                // The mixer has been capturing (and buffering) since StartAudioMixer(), well before this
                // point — encoder startup above can take several seconds. Discard that backlog now, right
                // as the segment's active-time clock starts, so the pump's first reads are live audio from
                // this instant rather than a stale replay of whatever was captured while waiting for the
                // encoder to connect. See IAudioMixer.ClearBuffers's doc comment.
                _mixer.ClearBuffers();
                StartAudioPumpThread(audioPipe, _stateMachine.Elapsed);
            }

            Log.Information("Recording started: {Enc} {W}x{H}@{Fps} safe={Safe} audio={Audio} -> {Path}",
                encoder.FfmpegId, dstW, dstH, fps, _safeRemux, audioEnabled, _finalPath);
            return true;
        }
        catch (Exception ex)
        {
            _errors.Block("record.start-failed", "Couldn't start the recording.", "See the log for details.", ex);
            SafeTeardown();

            // _stateMachine.StartRecording() above already transitioned to Recording by the time a *later*
            // step in this same try (ClearBuffers, the pacer thread, the audio pump) throws — without this,
            // the state machine is stuck there for the rest of the process: every future Start() call sees
            // IsBusy and is silently rejected, and Stop() would try to finalize a session that SafeTeardown()
            // already tore down. Recording/Paused -> Finalizing -> Idle is the only legal path back; drive it
            // explicitly since SafeTeardown() has already released every real resource, so this is purely
            // state-machine bookkeeping at this point, not a real finalization.
            try
            {
                if (_stateMachine.State is RecordingState.Recording or RecordingState.Paused)
                {
                    _stateMachine.Stop();
                }
                if (_stateMachine.State == RecordingState.Finalizing)
                {
                    _stateMachine.CompleteFinalization();
                }
            }
            catch (InvalidOperationException recoveryEx)
            {
                // Best-effort: a failure recovering state-machine bookkeeping shouldn't mask the original
                // start failure already reported above.
                Log.Warning(recoveryEx, "Couldn't recover the recording state machine after a failed Start()");
            }

            return false;
        }
    }

    /// <summary>Webcam-as-source doesn't run through WGC/D3D11 at all (see <see cref="WebcamCaptureEngine"/>'s
    /// class doc comment), so it needs its own concrete engine rather than <see cref="_captureFactory"/>'s
    /// always-<see cref="WgcCaptureEngine"/> default.</summary>
    private ICaptureEngine CreateCaptureEngine(CaptureTarget target) =>
        target.Kind == CaptureKind.Webcam ? new WebcamCaptureEngine() : _captureFactory();

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

        string outputDir = _paths.ResolveUserPath(_settings.Current.OutputFolder) ?? _paths.RecordingsDirectory;
        try
        {
            Directory.CreateDirectory(outputDir);
            // Creating an existing directory does not prove that this account can create the recording file
            // inside it (a common configuration for network shares). Verify that explicitly, before starting
            // capture and reporting a misleading encoder-start error later.
            string probePath = Path.Combine(outputDir, $".recmode-write-probe-{Guid.NewGuid():N}.tmp");
            try
            {
                using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose))
                {
                }
            }
            finally
            {
                // DeleteOnClose is the normal path; this also handles file systems that do not honor it.
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
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
        // Webcam-as-source already IS the webcam feed — overlaying a second webcam session onto itself would
        // be nonsensical (and WebcamCaptureEngine.SetWebcamOverlay is a documented no-op stub anyway).
        if (_originalTarget?.Kind == CaptureKind.Webcam)
        {
            return;
        }

#if RECMODE_SELFTEST
        if (_testForcedWebcamSource is { } forcedSource)
        {
            (int fx, int fy, int fw, int fh) = WebcamOverlayLayout.ComputeRect(
                dstW, dstH, _settings.Current.WebcamSizePercent, _settings.Current.WebcamPosition);
            _capture!.SetWebcamOverlay(forcedSource, new RegionRect(fx, fy, fw, fh));
        }
        else
#endif
        if (_settings.Current.WebcamEnabled && !string.IsNullOrEmpty(_settings.Current.WebcamDeviceId))
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
            catch (Exception ex)
            {
                // Deliberately broad: this is documented as best-effort, never blocking the recording, but a
                // narrower filter (InvalidOperationException/UnauthorizedAccessException/COMException) missed
                // FileNotFoundException — what MediaCapture.InitializeAsync actually throws for a device ID
                // that's since been unplugged — which escaped into Start()'s own catch and blocked the entire
                // recording instead of just warning, reachable headlessly via --tray + hotkey or a schedule
                // where the "No camera detected" UI guard never runs.
                _webcamCapture = null;
                Log.Warning(ex, "Webcam overlay failed to start");
                _errors.Warn("record.webcam-unavailable", "The webcam overlay couldn't be started.",
                    "Recording will continue without the picture-in-picture.");
            }
        }
    }

    /// <summary>Called by <see cref="RecordViewModel.IsAnnotating"/> when draw-on-screen annotation toggles.
    /// A no-op unless the recording's source is a Window (Monitor/Region/AllDisplays already see overlay
    /// windows naturally, per <see cref="WindowRegionProxy"/>'s doc comment). Just queues the swap —
    /// <see cref="PaceLoop"/> applies it on the pacer thread so <see cref="_capture"/> is never mutated
    /// cross-thread. See <see cref="SetClickHighlightActive"/>/<see cref="SetKeystrokeVisualizerActive"/> for
    /// the two sibling overlay features that need the identical substitution.</summary>
    public void SetAnnotating(bool isAnnotating)
    {
        if (_originalTarget is not { Kind: CaptureKind.Window } original)
        {
            return;
        }

        bool wasNeeded = NeedsWindowOverlayProxy;
        _isAnnotating = isAnnotating;
        ApplyWindowOverlayProxyChange(original, wasNeeded);
    }

    /// <summary>Called by <see cref="RecordViewModel.NotifyClickHighlightActive"/> (in turn driven by
    /// <see cref="ClickHighlightService"/> showing/hiding the click-ripple overlay) — same Window-source
    /// substitution <see cref="SetAnnotating"/> needs and for the same reason (see
    /// <see cref="NeedsWindowOverlayProxy"/>'s doc comment): without it, a Window-source recording showed
    /// ripples live on screen but they never actually appeared in the recorded file, since the ripple overlay
    /// is a separate window WGC's per-window capture can't see.</summary>
    public void SetClickHighlightActive(bool active)
    {
        if (_originalTarget is not { Kind: CaptureKind.Window } original)
        {
            return;
        }

        bool wasNeeded = NeedsWindowOverlayProxy;
        _clickHighlightActive = active;
        ApplyWindowOverlayProxyChange(original, wasNeeded);
    }

    /// <summary>Called by <see cref="RecordViewModel.NotifyKeystrokeVisualizerActive"/> — see
    /// <see cref="SetClickHighlightActive"/>'s doc comment; identical reasoning, different overlay.</summary>
    public void SetKeystrokeVisualizerActive(bool active)
    {
        if (_originalTarget is not { Kind: CaptureKind.Window } original)
        {
            return;
        }

        bool wasNeeded = NeedsWindowOverlayProxy;
        _keystrokeVisualizerActive = active;
        ApplyWindowOverlayProxyChange(original, wasNeeded);
    }

    /// <summary>Shared by <see cref="SetAnnotating"/>/<see cref="SetClickHighlightActive"/>/
    /// <see cref="SetKeystrokeVisualizerActive"/>: swaps to (or back from) the Region-proxy substitution only
    /// on an actual true→false or false→true transition of the *combined* need — so, e.g., turning off click
    /// highlighting while annotation is still active doesn't revert the live capture back to the real window
    /// (which would make the still-active annotation ink invisible in the recording again).</summary>
    private void ApplyWindowOverlayProxyChange(CaptureTarget original, bool wasNeeded)
    {
        bool isNeeded = NeedsWindowOverlayProxy;
        if (isNeeded == wasNeeded)
        {
            return;
        }

        if (!isNeeded)
        {
            _pendingRetarget = original;
            return;
        }

        if (CaptureCapabilities.TryGetWindowScreenRect(original.Handle, out RegionRect windowRect) &&
            WindowRegionProxy.Resolve(windowRect, CaptureCapabilities.EnumerateMonitors()) is { } proxy)
        {
            _pendingRetarget = proxy;
        }
    }

    /// <summary>Window-source recordings only, called from <see cref="PaceLoop"/>: if the recorded window's
    /// on-screen size has changed since the capture last (re)started, queues a retarget at the same window so
    /// <see cref="RetargetCapture"/> re-reads its current size — the video stays scaled to the fixed encoder
    /// output, so the whole window is always visible instead of a stale, wrongly-sized crop. Skipped while any
    /// overlay feature needs the Region-proxy substitution (the live capture isn't the window itself then —
    /// see <see cref="NeedsWindowOverlayProxy"/>) or while a retarget is already pending, so this never
    /// clobbers that swap.</summary>
    private void CheckWindowResize()
    {
        if (NeedsWindowOverlayProxy || _pendingRetarget is not null || _originalTarget is not { Kind: CaptureKind.Window } original)
        {
            return;
        }

        if (CaptureCapabilities.TryGetWindowScreenRect(original.Handle, out RegionRect rect) &&
            rect.Width > 0 && rect.Height > 0 &&
            (rect.Width != _lastWindowW || rect.Height != _lastWindowH))
        {
            // Deliberately NOT committed to _lastWindowW/_lastWindowH here — only once RetargetCapture
            // actually applies this size (see the pacer loop). Committing eagerly meant a single transient
            // RetargetCapture failure (the window closing mid-swap, a momentary capture-engine error) marked
            // this size as "already handled" forever, even though the live capture never actually caught up —
            // follow-window-resize silently stopped working for the rest of the recording after that one
            // failure, with the output stuck at a stale, wrongly-sized crop.
            _pendingResizeSize = (rect.Width, rect.Height);
            _pendingRetarget = original;
        }
    }

    /// <summary>Swaps the live <see cref="_capture"/> engine for one pointed at <paramref name="target"/>,
    /// keeping the same encoder output size so the pacer's frame buffer stays valid across the swap. Builds
    /// and starts the replacement fully before tearing down the old one, so a failure (e.g. the window closed)
    /// leaves the original capture running instead of losing capture entirely. Pacer-thread-only — see
    /// <see cref="SetAnnotating"/>.</summary>
    private bool RetargetCapture(CaptureTarget target)
    {
        ICaptureEngine? next = null;
        try
        {
            next = _captureFactory();
            next.Faulted += OnCaptureFaulted;
            next.Start(target, _dstW, _dstH, _settings.Current.CaptureCursor, _targetFps);
            next.SetBrightness(_settings.Current.Brightness);
#if RECMODE_SELFTEST
            IWebcamFrameSource? webcamSource = _testForcedWebcamSource ?? (IWebcamFrameSource?)_webcamCapture;
#else
            IWebcamFrameSource? webcamSource = (IWebcamFrameSource?)_webcamCapture;
#endif
            if (webcamSource is not null)
            {
                (int wx, int wy, int ww, int wh) = WebcamOverlayLayout.ComputeRect(
                    _dstW, _dstH, _settings.Current.WebcamSizePercent, _settings.Current.WebcamPosition);
                next.SetWebcamOverlay(webcamSource, new RegionRect(wx, wy, ww, wh));
            }
        }
        catch (Exception ex)
        {
            // next can be fully constructed (a live D3D11 device/WGC session, Faulted already subscribed)
            // even when a later step in this same try throws — leaving it undisposed leaked one such engine
            // per failed retarget, and kept it rooted for the rest of the process via the still-subscribed
            // Faulted handler. Every failed attempt used to leak, not just a rare one.
            if (next is not null)
            {
                next.Faulted -= OnCaptureFaulted;
                next.Dispose();
            }
            _errors.Warn("record.annotate-retarget-failed",
                "Couldn't switch capture for drawing — the recording will continue without it.",
                "Try again, or switch to Monitor/Region capture to draw on the recording.", ex);
            return false;
        }

        ICaptureEngine old;
        lock (_captureAccessLock)
        {
            // The swap itself must be inside the lock, not just the assignment: SetBrightness/SetZoomTarget/
            // SetBaseRect/CaptureSupportsZoom take this same lock around their entire _capture?.Xxx() call, so
            // whichever side gets there first — a caller mid-call on `old`, or this swap — fully finishes
            // before the other proceeds. Without that mutual exclusion, a caller could still be inside
            // old.SetBrightness(...) at the exact moment old.Stop()/Dispose() run below.
            old = _capture!;
            _capture = next;
        }
        old.Faulted -= OnCaptureFaulted;
        old.Stop();
        old.Dispose();
        return true;
    }

    /// <summary>Raised from the capture engine's background (DDA/WGC) thread when it hits an unrecoverable
    /// error (or the captured window/display was closed) but has already shut itself down safely. Ends the
    /// recording rather than letting it keep running: once the capture engine is dead, PaceLoop has no way to
    /// produce a new frame, so it would otherwise silently duplicate the last one, at full fps, for the rest
    /// of the recording — a full-length file that's frozen from this instant on, with nothing to tell the
    /// user. Stopping means they get a shorter but honest recording of what was actually captured.</summary>
    private void OnCaptureFaulted(object? sender, Exception ex)
    {
        Log.Warning(ex, "Capture engine faulted");
        _errors.Warn("record.capture-faulted", "The screen capture stopped — the recording was ended.",
            "The captured window or display was closed, or the capture device was lost. What was recorded up to this point is saved.");

        if (_stateMachine.IsBusy)
        {
            System.Threading.Tasks.Task.Run(Stop); // never call Stop() inline from this callback's own thread
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
        (_finalPath, _recordingPath) = _safeRemux
            ? BuildSafeRecordingPaths(outputDir, fileName)
            : (FilenameBuilder.BuildUniquePath(outputDir, fileName), "");
        if (!_safeRemux)
        {
            _recordingPath = _finalPath;
        }
        _ffmpegPath = ffmpegPath;

        // Auto-split bookkeeping: remember enough to rebuild subsequent segment file names/paths.
        _outputDir = outputDir;
        _baseFileName = fileName;
        _segmentIndex = 1;
        _lastRotatedSegmentResult = null; // defensive; Finalize() already clears it on every normal path
        _autoSplitEnabled = _settings.Current.AutoSplitEnabled;
        _autoSplitThresholdBytes = Math.Max(100, _settings.Current.AutoSplitSizeMb) * 1024L * 1024L;
        _downgradeAttempted = false;
#if RECMODE_SELFTEST
        _testForceDowngrade = false;
#endif

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
        // The pipe's DACL (FfmpegRecordingSession.CreateSecurePipe) is what actually keeps other accounts
        // out; the GUID suffix is defense-in-depth so the name itself isn't derivable from public process
        // info (pid + uptime), same reasoning as the old name being enumerable at \\.\pipe\.
        string? audioPipeName = audioEnabled ? $"recmode_aud_{Guid.NewGuid():N}" : null;

        var job = new FfmpegJob
        {
            Encoder = encoder,
            Container = actualContainer,
            Width = dstW,
            Height = dstH,
            FrameRate = fps,
            Quality = quality,
            PipeName = $"recmode_vid_{Guid.NewGuid():N}",
            OutputPath = _recordingPath,
            AudioPipeName = audioPipeName,
            AudioCodec = _settings.Current.AudioCodec,
            AudioBitrateKbps = _settings.Current.AudioBitrateKbps,
            CpuThreadCap = _settings.Current.CpuThreadCap,
            BelowNormalPriority = _settings.Current.BelowNormalEncoderPriority,
            Effort = _settings.Current.Effort,
            BitrateGuardrailEnabled = _settings.Current.BitrateGuardrailEnabled,
            IsScreenContent = target.Kind != CaptureKind.Webcam,
            // Under safe recording the muxer writes a temp MKV, but audio-args steering must follow the
            // container the user actually picked — Opus/FLAC in the temp MKV can't be stream-copied into
            // MP4/MOV on remux, so the steering (which would have forced AAC for MP4/MOV without safe
            // recording) has to see that final container. See FfmpegJob.FinalContainer.
            FinalContainer = _safeRemux ? container : null,
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
        bool perAppTargetMissing = false;
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
                // Deliberately zeroed BEFORE calling Start (fail closed — see below), which means
                // AudioMixerStartResult.SystemRequested/SystemDegraded can never observe the original
                // request: they'd read false/false regardless of whether the target was actually missing,
                // silently swallowing the exact warning this whole fail-closed path exists to surface. Track
                // it here instead and warn directly, with a message that actually names the missing process
                // rather than the generic one below.
                captureSystem = false;
                perAppTargetMissing = true;
            }
        }

        _mixer = _mixerFactory();
        AudioMixerStartResult startResult = _mixer.Start(captureSystem, _settings.Current.MicrophoneEnabled, perAppPid,
            systemDeviceIds: _settings.Current.SystemAudioDeviceIds,
            captureCommsRoleAudio: _settings.Current.CaptureCommunicationsRoleAudio);
        _mixer.SystemGain = _settings.Current.SystemVolume / 100f;
        _mixer.MicGain = _settings.Current.MicVolume / 100f;

        // A requested audio source that failed to start is not fatal — recording continues without it —
        // but silently dropping it would leave the user wondering why the file has no audio.
        if (perAppTargetMissing)
        {
            _errors.Warn("record.audio-system-unavailable",
                $"System audio couldn't be captured — \"{targetName}\" isn't running.",
                "The recording will continue without system audio.");
        }
        else if (startResult.SystemDegraded)
        {
            _errors.Warn("record.audio-system-unavailable",
                "System audio couldn't be captured for this recording.",
                "The recording will continue without system audio.");
        }

        if (startResult.MicDegraded)
        {
            _errors.Warn("record.audio-mic-unavailable",
                "The microphone couldn't be captured for this recording.",
                "The recording will continue without microphone audio.");
        }
    }

    /// <summary>Stops the current recording and finalizes the file.</summary>
    public void Stop()
    {
        if (!_stateMachine.IsBusy)
        {
            return;
        }

        if (!TryClaimFinalize())
        {
            // The pacer thread's own fatal-error path (HandleFatalPipeBreak) already claimed finalize —
            // e.g. the encoder died right as Stop() was called. It already drove the state machine to Idle
            // and raised Finished; nothing more to do here.
            if (_stateMachine.IsBusy && _pacer != Thread.CurrentThread)
            {
                _finalizationCompleted.Wait();
            }
            return;
        }

        if (_stateMachine.State is RecordingState.Recording or RecordingState.Paused)
        {
            _stateMachine.Stop();
            // Finalize() below (remux for safe-recording, library-index write, filesystem moves) can take
            // real time — nothing else raises progress with State==Finalizing, so without this the UI
            // freezes on its last Recording-state snapshot for however long that takes, then jumps straight
            // to Idle when Finished fires. One explicit tick lets the UI show a genuine "Finalizing…" state
            // instead of silently doing nothing.
            RaiseProgress();
        }

        // Cancel active pipe writes before waiting. Teardown must never dispose capture/session resources
        // while the pacer or audio producer can still be using them.
        _stopRequested = true;
        try
        {
            // _session and _audioStop are owned by the pacer thread, which — right up until it observes
            // _stopRequested at the top of its next loop iteration — can still be mid-RotateSegment,
            // disposing the very objects these two calls read a moment earlier (RotateSegment disposes the
            // old _session before reassigning it, and StartAudioPumpThread now disposes the previous
            // rotation's _audioStop before creating a new one). A read here can win the race against that
            // disposal by a few instructions, turning "cancel" into an ObjectDisposedException. Since both
            // calls are just best-effort cancellation signals — the whole point of the Join() below is to
            // actually wait for the pacer to stop — an object that's already disposed is already being torn
            // down by the concurrent rotation, so there's nothing left to cancel. Previously this exception
            // escaped Stop() entirely: the Task.Run(Stop) wrapper (added when Stop() moved off the UI
            // thread) faulted silently, _finalizeStarted stayed true forever, _finalizationCompleted was
            // never Set, IsBusy got stuck true (no further recording possible), and App.OnExit's own Stop()
            // call then blocked forever on _finalizationCompleted.Wait() — the app had to be killed to exit.
            _session?.RequestStop();
            _audioStop?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_pacer is not null && _pacer != Thread.CurrentThread)
        {
            _pacer.Join();
        }

        // Anything escaping Finalize() used to skip CompleteFinalization() entirely, which left the state
        // machine parked in Finalizing forever: IsBusy stayed true, so Start()'s guard rejected every
        // subsequent recording for the rest of the process's life, and — because Stop() runs under Task.Run —
        // the user was never told why. Recording simply stopped working until they restarted the app.
        // Finalize() reaches a lot of fallible surface area (remux, ffmpeg exit handling, the library index,
        // filesystem moves), so the state transition and the Finished notification must be unconditional:
        // whatever went wrong, this recording is over and the app has to become usable again.
        try
        {
            RecordingResult result;
            try
            {
                result = Finalize();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Finalizing the recording failed");
                _errors.Fatal("record.finalize-failed", "The recording couldn't be finalized.",
                    "The captured file may still be in the output folder. Check the log for details.", ex);
                result = new RecordingResult(false, -1, _finalPath, 0);
            }

            if (_stateMachine.State == RecordingState.Finalizing)
            {
                _stateMachine.CompleteFinalization();
            }

            Finished?.Invoke(result);
        }
        finally
        {
            _finalizationCompleted.Set();
        }
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

    /// <summary>Pauses the recording — the pacer stops writing; output has no gap for the paused span (§3.7).
    /// Has two independent callers on two threads (the UI/hotkeys and the pacer thread's own disk-critical
    /// guard); uses <see cref="RecordingStateMachine.TryPause"/> so losing that race is a silent no-op
    /// instead of an unhandled exception that would otherwise force-finalize the recording.</summary>
    public void Pause()
    {
        if (_stateMachine.TryPause())
        {
            RaiseProgress();
        }
    }

    /// <summary>Resumes a paused recording. See <see cref="Pause"/> for why this tolerates a lost race.</summary>
    public void Resume()
    {
        // Same backlog problem as Start()'s (see IAudioMixer.ClearBuffers): WASAPI capture never actually
        // stops while paused — only the pump's consumption does, since its segmentElapsed() callback freezes
        // with the state machine — so the buffer quietly fills with paused-span audio for the whole pause.
        // Without this, resuming replayed however much of that as the first "post-resume" audio in the
        // output — including, disconcertingly, anything said while paused. Cleared *before* TryResume() so
        // the state machine's clock (and therefore the pump's target) only starts advancing again once the
        // buffer is already empty, rather than racing the pump thread's own poll of it.
        _mixer?.ClearBuffers();
        if (_stateMachine.TryResume())
        {
            RaiseProgress();
        }
    }

    public bool IsPaused => _stateMachine.State == RecordingState.Paused;

    /// <summary>Applies per-source gains (0..1) and mute state to the live recording mixer, so volume/mute
    /// changes take effect mid-recording. Mute is propagated here (not just as gain=0) because
    /// <see cref="IAudioMixer"/>'s meters read <c>Muted</c> directly and are otherwise computed from the raw
    /// pre-gain capture — without this, muting mid-recording left the meter still bouncing at full deflection
    /// even though nothing was being recorded, actively misrepresenting the one indicator that's documented
    /// as the mic test.</summary>
    public void SetAudioGains(float systemGain, float micGain, bool systemMuted = false, bool micMuted = false)
    {
        // No lock against Finalize() nulling _mixer concurrently, unlike _capture's _captureAccessLock —
        // deliberately, not an oversight. Finalize() only disposes _mixer after joining the audio-pump thread,
        // and AudioMixer's own members (post-0.9.111 copy-on-write rewrite) are safe to call on an
        // already-stopped instance: Gain/Muted are plain properties on MixSource objects whose managed state
        // outlives Dispose() by design (see MixSource.Dispose's doc comment), and SetMicEnabled no-ops once
        // IsRunning is false. Reading the `_mixer` field itself a moment before it's nulled is a benign,
        // harmless race — worst case, these four writes land on a mixer about to be thrown away.
        if (_mixer is { } mixer)
        {
            mixer.SystemGain = systemGain;
            mixer.MicGain = micGain;
            mixer.SystemMuted = systemMuted;
            mixer.MicMuted = micMuted;
        }
    }

    /// <summary>Called by <see cref="RecordViewModel.MicEnabled"/> when toggled mid-recording — direct user
    /// request. Previously the mic capture actually opened by <see cref="StartAudioMixer"/> was fixed for the
    /// whole recording (set once from <c>_settings.Current.MicrophoneEnabled</c> at <see cref="Start"/> time),
    /// so forgetting to enable the mic before hitting Record — or deciding partway through to turn it off —
    /// silently had no effect on the file actually being written, only on the *next* recording. A no-op if not
    /// currently recording. Warns (matching <see cref="StartAudioMixer"/>'s own degraded-source warning) if
    /// enabling fails — an unplugged mic or an exclusive-mode conflict from another app.</summary>
    public void SetMicEnabled(bool enabled)
    {
        if (_mixer is not { } mixer)
        {
            return;
        }

        bool started = mixer.SetMicEnabled(enabled);
        if (enabled && !started)
        {
            _errors.Warn("record.audio-mic-unavailable",
                "The microphone couldn't be captured for this recording.",
                "Recording will continue without microphone audio.");
        }
    }

    /// <summary>Applies the captured-video brightness adjustment (-100..100) to the live recording, so
    /// changes on the Record screen take effect mid-recording, not just on the next session.</summary>
    public void SetBrightness(double value)
    {
        lock (_captureAccessLock) { _capture?.SetBrightness(value); }
    }

    /// <summary>True if the active capture is actually on the GPU VideoProcessor pipeline, so a zoom target
    /// (auto or manual) has any effect. False when capture fell back to the GDI software path — checked before
    /// offering manual zoom's picker so the user isn't sent through a drag-select that can't do anything.</summary>
    public bool CaptureSupportsZoom
    {
        get { lock (_captureAccessLock) { return _capture?.SupportsZoom ?? false; } }
    }

    /// <summary>Smart auto-zoom: sets/clears the GPU pan-zoom target (source-local pixels; see
    /// <see cref="ComputeZoomRect"/> to derive one from a screen-space click).</summary>
    public void SetZoomTarget(RegionRect? rect)
    {
        lock (_captureAccessLock) { _capture?.SetZoomTarget(rect); }
    }

    /// <summary>
    /// Live-retargets a Region-source recording's actual captured area (source-local pixels) — dragging the
    /// on-screen contour to a new spot while recording, rather than a temporary zoom effect. Also updates the
    /// coordinator's own record of the active target so it reflects reality (matters for <see cref="Stop"/>'s
    /// bookkeeping and for <see cref="ComputeZoomRect"/> staying correct if auto/manual zoom is used afterward).
    /// </summary>
    public void SetBaseRect(RegionRect rect)
    {
        lock (_captureAccessLock) { _capture?.SetBaseRect(rect); }
        if (_originalTarget is { Kind: CaptureKind.Region } target)
        {
            _originalTarget = target with { Region = rect };
        }
    }

    /// <summary>
    /// Computes the GPU crop rect for an auto-zoom centered on a screen-space (physical, virtual-desktop)
    /// click point, or null if the click shouldn't trigger a zoom. <paramref name="zoomFactor"/> &gt; 1 shrinks
    /// the rect (zooms in) — e.g. 2.0 halves both dimensions.
    /// <para>Deliberately scoped to Monitor/Region sources only for v1: Window capture's rect moves/resizes
    /// independently of monitor coordinates (and can be resized/closed mid-recording), and All-Displays has no
    /// single fixed monitor origin to convert a click into — both would need their own coordinate-tracking
    /// logic rather than reusing the monitor-relative math below, so a click during those sources is silently
    /// treated as "no zoom" rather than half-implemented.</para>
    /// </summary>
    public RegionRect? ComputeZoomRect(int screenX, int screenY, double zoomFactor)
    {
        if (_originalTarget is not { } target || target.Kind is CaptureKind.Window or CaptureKind.AllDisplays)
        {
            return null;
        }

        MonitorInfo? mon = ResolveZoomMonitor(target.Handle);
        if (mon is null)
        {
            return null;
        }

        RegionRect bounds = target.Region ?? new RegionRect(0, 0, mon.Width, mon.Height);
        int localX = screenX - mon.X;
        int localY = screenY - mon.Y;
        if (!AutoZoomMath.Contains(bounds, localX, localY))
        {
            return null; // click landed outside the captured area
        }

        return AutoZoomMath.ComputeZoomRect(bounds, localX, localY, zoomFactor);
    }

    /// <summary>Caches the resolved monitor per target handle. <see cref="CaptureCapabilities.EnumerateMonitors"/>
    /// re-probes DXGI/HDR state for every monitor on the system on every call (a real COM/DXGI cost, not just
    /// a Win32 EnumDisplayMonitors walk) — and the target monitor for an active Monitor/Region recording never
    /// changes mid-recording (only its Region sub-rect can, via <see cref="SetBaseRect"/>, which doesn't touch
    /// the monitor), so re-resolving it on every single mouse click during smart auto-zoom was pure waste.</summary>
    private MonitorInfo? ResolveZoomMonitor(nint handle)
    {
        if (_zoomMonitorCache is null || _zoomMonitorCacheHandle != handle)
        {
            _zoomMonitorCacheHandle = handle;
            _zoomMonitorCache = CaptureCapabilities.EnumerateMonitors().FirstOrDefault(m => m.Handle == handle);
        }
        return _zoomMonitorCache;
    }

    /// <summary>Starts the audio-pump thread for the given pipe — shared by <see cref="Start"/> and
    /// <see cref="RotateSegment"/> (auto-split / hw→sw downgrade), which each open a fresh audio pipe per
    /// segment. Runs on a dedicated background <see cref="Thread"/> (not a <see cref="System.Threading.Tasks.Task"/>):
    /// anything unhandled here would otherwise be a genuinely unhandled thread exception, which terminates
    /// the whole process immediately — so the catch is deliberately broad. The video pacer is the source of
    /// truth for stopping the recording; losing audio mid-recording is degraded, not fatal, so this just logs
    /// and stops pumping.
    /// <para><paramref name="segmentStartedAt"/> must be the state machine's Elapsed at the moment this
    /// segment's *video* frame-0 will land — i.e. captured before any finalize/remux delay, not after. The
    /// pacer's Elapsed-driven catch-up (§3.3 CFR policy) means a new segment's early video frames are stamped
    /// with PTS covering the whole rotation gap, all written back-to-back the instant the pacer resumes; the
    /// audio pump must anchor its own PTS=0 to that same pre-gap instant, or real audio ends up shifted ahead
    /// of the video content it was recorded alongside by the length of the gap.</para></summary>
    private void StartAudioPumpThread(NamedPipeServerStream audioPipe, TimeSpan segmentStartedAt)
    {
        // Disposes the previous rotation's CancellationTokenSource. By the time this runs — either from
        // Start() the first time (where _audioStop is still null, so this is a no-op) or from RotateSegment
        // on every later segment — any prior audio thread has already been joined (RotateSegment's own
        // _audioThread.Join() above runs before this is called), so the old CTS is safe to dispose here
        // instead of being orphaned until Finalize()/SafeTeardown() disposes only whichever one is current
        // when the whole recording ends.
        _audioStop?.Dispose();
        _audioStop = new CancellationTokenSource();
        CancellationTokenSource stopSource = _audioStop;
        _audioThread = new Thread(() =>
        {
            try
            {
                if (!WaitForAudioPipeConnection(audioPipe, stopSource.Token))
                {
                    return; // ffmpeg never connected (dead encoder) or the recording ended first — nothing to pump
                }

                // The offset is read once here, at pump start, rather than per-iteration: it's applied as
                // leading silence/discard at the head of the stream, so changing it mid-recording couldn't
                // take effect anyway, and re-reading it would only invite a torn read of a live setting.
                int syncOffsetMs = ClampAudioSyncOffsetMs(_settings.Current.AudioSyncOffsetMs);
                _mixer!.PumpUntil(audioPipe, () =>
                {
                    TimeSpan elapsed = _stateMachine.Elapsed - segmentStartedAt;
                    return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
                }, stopSource.Token, syncOffsetMs);
            }
            catch (OperationCanceledException) when (stopSource.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "Audio pump ended");
            }
        }) { IsBackground = true, Name = "recmode-audio" };
        _audioThread.Start();
    }

    /// <summary>Bounded, cancellable wait for ffmpeg to open the audio pipe. The plain synchronous
    /// <c>NamedPipeServerStream.WaitForConnection()</c> this replaces has no timeout and isn't cancellation-aware
    /// — <see cref="FfmpegRecordingSession.Start"/> guards the exact same hazard on the *video* pipe ("otherwise
    /// WaitForConnection would deadlock") but the audio pipe never got the same treatment. If ffmpeg dies (or
    /// never gets far enough to probe the audio stream — it opens inputs in order, and needs a real video frame
    /// first) before connecting, the old code stranded this thread forever, and every join against it
    /// (<see cref="Finalize"/>, <see cref="RotateSegment"/>) was itself unbounded — so a single stuck audio
    /// connection could freeze <see cref="Stop"/> for the whole app, exactly the class of hang already fixed
    /// once for the pacer thread. 8 s mirrors the video pipe's own connect timeout.</summary>
    private static bool WaitForAudioPipeConnection(NamedPipeServerStream pipe, CancellationToken token)
    {
        System.Threading.Tasks.Task connect = pipe.WaitForConnectionAsync(token);
        try
        {
            connect.Wait(TimeSpan.FromSeconds(8), token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (AggregateException)
        {
            return false; // the pipe faulted while waiting (e.g. disposed out from under it)
        }

        return connect.IsCompletedSuccessfully;
    }

    private void PaceLoop(int fps)
    {
        byte[] frame = new byte[_capture!.Nv12ByteSize];
        int lumaLength = _capture.OutputWidth * _capture.OutputHeight;
        long framesWritten = 0;
        long lastReport = Stopwatch.GetTimestamp();
        long blackSince = 0;
        bool blackWarned = false;
        var health = new PacerHealthTracker(Stopwatch.Frequency);
        long lastDiskCheck = Stopwatch.GetTimestamp();
        long lastSplitCheck = Stopwatch.GetTimestamp();
        long lastWindowCheck = Stopwatch.GetTimestamp();
        long lastStaleCaptureCheck = Stopwatch.GetTimestamp();
        long captureStartedAt = lastStaleCaptureCheck;
        long lastCapturedFrameCount = _capture.CapturedFrameCount;
        long lastCapturedFrameChangeAt = captureStartedAt;
        long lastAudioFaultCheck = Stopwatch.GetTimestamp();
        bool systemAudioFaultWarned = false;
        bool micAudioFaultWarned = false;
        long lastFrameSequence = -1;

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
                    double elapsed = _stateMachine.Elapsed.TotalSeconds;
                    double nextFrameElapsed = (framesWritten + 1) / (double)fps;
                    double sleepSeconds = nextFrameElapsed - elapsed;
                    if (sleepSeconds > 0.001)
                    {
                        Thread.Sleep((int)Math.Max(1, sleepSeconds * 1000));
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                    continue;
                }

                // Initial capture liveness check must run even while TryGetLatestFrame has no image yet;
                // otherwise the no-first-frame path's early continue would bypass the watchdog entirely.
                long preFrameNow = Stopwatch.GetTimestamp();
                if (framesWritten == 0 && preFrameNow - captureStartedAt > 10 * Stopwatch.Frequency)
                {
                    _errors.Warn("record.capture-stalled",
                        "The screen capture did not deliver an initial frame — the recording was ended.",
                        "What was recorded up to this point is saved.");
                    _stopRequested = true;
                    _ = System.Threading.Tasks.Task.Run(Stop);
                    break;
                }

                long currentSeq = _capture.FrameSequence;
                if (currentSeq != lastFrameSequence)
                {
                    if (!_capture.TryGetLatestFrame(frame))
                    {
                        Thread.Sleep(1);
                        continue; // no first frame yet
                    }
                    lastFrameSequence = currentSeq;
                }

                _session!.WriteFrame(frame, frame.Length);
                framesWritten++;

                long now = Stopwatch.GetTimestamp();

                // Black-frame watchdog (§3.6): exclusive-fullscreen games, DRM-protected windows, and video
                // players/browsers rendering through a hardware overlay surface all capture as black under
                // Windows.Graphics.Capture — the compositor never draws that surface into the frame WGC hands
                // us, so there's no pixel data to fix in our own pipeline. The one actionable remediation for
                // the overlay case is disabling hardware-accelerated/overlay video decode in the source app.
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
                                "The recording looks black.",
                                "Exclusive-fullscreen games and DRM-protected windows can't be captured — switch to borderless/windowed mode. " +
                                "A video window that plays fine but records black is usually rendering through a hardware overlay; disabling " +
                                "hardware acceleration / overlay scaling for video playback in that app fixes it.");
                        }
                    }
                    else
                    {
                        blackSince = 0;
                    }
                }

                // Health (§3.6 recording health): if the encoder can't keep up, WriteFrame back-pressures and
                // we fall > 1 s behind real time. Sustained → Degraded, then a hw→sw downgrade. The
                // hysteresis itself lives in PacerHealthTracker so it's independently testable — see its doc
                // comment for why (this logic has already regressed once in a way no test could reach).
                switch (health.Evaluate(now, _stateMachine.Elapsed.TotalSeconds, framesWritten, fps,
                            _activeEncoder?.IsHardware ?? false))
                {
                    case PacerHealthAction.Degrade:
                        _errors.Degrade("record.encoder-slow",
                            "The encoder can't keep up — the recording may run slow.",
                            "Try a lower resolution or frame rate, or a hardware encoder.");
                        break;

                    case PacerHealthAction.DowngradeToSoftware:
                        // Mid-stream hw→sw fallback: switch the hardware encoder out for a software one on a
                        // fresh segment (once per recording — AttemptDowngrade enforces that itself). Only
                        // reset the health tracker's grace period if a rotation actually happened — otherwise
                        // (already attempted once, or no software fallback exists for this codec/container)
                        // ResetAfterRotation() cleared IsBehind unconditionally, so the health indicator
                        // flickered back to "healthy" every single tick from then on even though the encoder
                        // was still — and would keep — falling behind real time, misreporting the exact case
                        // §3.6's recording-health signal exists to catch.
                        if (AttemptDowngrade() is { } downgradeRotationDuration)
                        {
                            health.ResetAfterRotation(downgradeRotationDuration); // fresh, gap-sized grace period for the new encoder
                        }
                        break;
                }

                _encoderBehind = health.IsBehind;

#if RECMODE_SELFTEST
                // Test-only seam (--selftest-downgrade): force the same rotation the health check would trigger.
                if (_testForceDowngrade)
                {
                    _testForceDowngrade = false;
                    AttemptDowngrade();
                }
#endif

                // Follow window resize (Window source only): WGC's capture item is sized once, when the
                // engine (re)starts — it doesn't itself track later resizes of the window it's pointed at.
                // Polled here (one cheap GetWindowRect call) rather than hooked, mirroring the disk-space/
                // auto-split checks already in this loop. A detected size change queues the same hot-swap
                // SetAnnotating uses below, so the live capture re-reads the window's current size and keeps
                // showing all of it — scaled to the fixed encoder output — instead of a stale crop.
                if (now - lastWindowCheck >= Stopwatch.Frequency / 4) // ~4 Hz
                {
                    lastWindowCheck = now;
                    CheckWindowResize();
                }

                // Draw-on-screen annotation toggled for a Window-source recording (see SetAnnotating): swap
                // the live capture in/out of the Region-proxy substitution here, on the pacer thread.
                if (_pendingRetarget is { } pendingRetarget)
                {
                    _pendingRetarget = null;
                    (int W, int H)? resizeSize = _pendingResizeSize;
                    _pendingResizeSize = null;
                    // Only commit the new size as "seen" if the swap actually applied it — see
                    // CheckWindowResize's comment for why eagerly committing on failure permanently broke
                    // follow-window-resize.
                    if (RetargetCapture(pendingRetarget) && resizeSize is { } size)
                    {
                        _lastWindowW = size.W;
                        _lastWindowH = size.H;
                    }
                }

                // Stale-capture watchdog: defense-in-depth alongside OnCaptureFaulted. Faulted covers the
                // engine's own known unrecoverable-error and window-closed paths, but if the capture engine
                // ever stops producing new frames without raising it (a case Faulted doesn't cover), PaceLoop
                // would otherwise duplicate the same last frame forever for the rest of the recording with
                // nothing to catch it — a full-length file that's frozen from that instant on, with no
                // warning to the user.
                //
                // This used to re-check the exact same "framesWritten == 0" predicate the per-iteration
                // bootstrap check above already covers — which can never fire here, since the bootstrap check
                // always trips (and breaks the loop) first. That silently narrowed this watchdog down to
                // "detect failure to deliver the FIRST frame" only, losing all coverage for capture dying
                // partway through an otherwise-healthy recording — exactly the case this comment used to
                // describe covering. CapturedFrameCount (real captures, independent of framesWritten's
                // CFR-duplicated count) is the right signal instead: WGC is change-driven, so it legitimately
                // stays flat on a static desktop, which is why the threshold is generous (60 s, not 10 s) —
                // long enough that a real static desktop essentially never trips it, short enough to still
                // catch a genuinely dead capture within a reasonable time.
                if (now - lastStaleCaptureCheck >= Stopwatch.Frequency) // ~1 Hz
                {
                    lastStaleCaptureCheck = now;
                    long capturedNow = _capture.CapturedFrameCount;
                    if (capturedNow != lastCapturedFrameCount)
                    {
                        lastCapturedFrameCount = capturedNow;
                        lastCapturedFrameChangeAt = now;
                    }
                    else if (framesWritten > 0 && now - lastCapturedFrameChangeAt > 60 * Stopwatch.Frequency)
                    {
                        _errors.Warn("record.capture-stalled",
                            "The screen capture stopped producing new frames — the recording was ended.",
                            "What was recorded up to this point is saved.");
                        _stopRequested = true;
                        System.Threading.Tasks.Task.Run(Stop); // never call Stop() inline from the pacer thread
                        break;
                    }
                }

                // Mid-recording audio device failure (§3.6 DegradedState): a genuine WASAPI capture failure
                // (device unplugged, exclusive-mode conflict, endpoint invalidated) used to be logged only —
                // the mixer keeps "reading" zero-filled silence from the dead source for the rest of the
                // recording with no indication anything failed, the same failure shape as the historical
                // full-system-audio-silence bug, just triggered mid-recording instead of at start. Recording
                // continues (video is unaffected and the other audio source, if any, is fine) — this only
                // surfaces the warning once per source, it doesn't stop anything.
                if (_mixer is not null && now - lastAudioFaultCheck >= Stopwatch.Frequency) // ~1 Hz
                {
                    lastAudioFaultCheck = now;
                    if (!systemAudioFaultWarned && _mixer.SystemFaulted)
                    {
                        systemAudioFaultWarned = true;
                        _errors.Warn("record.audio-system-failed",
                            "System audio stopped mid-recording.",
                            "The rest of the recording will have no system audio. Check the log for details.");
                    }

                    if (!micAudioFaultWarned && _mixer.MicFaulted)
                    {
                        micAudioFaultWarned = true;
                        _errors.Warn("record.audio-mic-failed",
                            "The microphone stopped mid-recording.",
                            "The rest of the recording will have no microphone audio. Check the log for details.");
                    }
                }

                // Auto-pause safety guard, disk half (§3.6): pause rather than stop outright before a full disk
                // corrupts the finish — pausing writes no more frames (so it can't make the problem worse) but
                // keeps the recording resumable once space is freed, instead of force-finalizing it. Pause()
                // is safe to call inline (unlike Stop(), it doesn't join this thread); the next loop iteration's
                // Paused early-continue stops re-checking until actually resumed.
                if (now - lastDiskCheck >= 2 * Stopwatch.Frequency) // every ~2 s
                {
                    lastDiskCheck = now;
                    if (IsDiskCriticallyLow())
                    {
                        _errors.Warn("record.disk-critical",
                            "Recording paused — the disk is nearly full.",
                            "Free up space, then resume. It'll pause again if space is still critically low.");
                        Pause();
                        continue;
                    }
                }

                // Auto-split (§3.3): roll to a new segment file once the current one crosses the threshold.
                if (_autoSplitEnabled && now - lastSplitCheck >= Stopwatch.Frequency) // ~1 Hz
                {
                    lastSplitCheck = now;
                    if (TryGetSegmentSize(out long segSize) && segSize >= _autoSplitThresholdBytes)
                    {
                        long rotationStart = Stopwatch.GetTimestamp();
                        RotateSegment();
                        TimeSpan rotationDuration = Stopwatch.GetElapsedTime(rotationStart);

                        // A rotation blocks this thread for however long finalize+safe-remux+encoder-restart
                        // takes, during which framesWritten falls behind Elapsed·fps through no fault of the
                        // encoder's actual per-frame throughput — the pacer simply wasn't running. Without
                        // this, the health check below could immediately (mis)read that gap as 3+ seconds of
                        // "the encoder can't keep up," firing the Degraded toast or even the hw→sw downgrade
                        // for a perfectly healthy encoder on every large-file auto-split. Resetting here gives
                        // it a fresh grace window measured from after the rotation, same as the mid-stream
                        // hw→sw downgrade path already does for its own rotation just below — sized to the
                        // rotation's own actual duration (not a fixed window), since the catch-up burst the
                        // pacer must drain afterward is itself proportional to how long the rotation took (a
                        // slow-disk remux can take far longer than any fixed grace window would assume).
                        health.ResetAfterRotation(rotationDuration);
                        _encoderBehind = false;
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
        if (!TryClaimFinalize())
        {
            // Stop() (UI thread) already claimed finalize concurrently — almost always because this is the
            // exact, entirely normal race where Stop() sets _stopRequested and cancels the pipe write
            // (FfmpegRecordingSession.RequestStop) a moment before this thread's own in-flight WriteFrame call
            // observes that cancellation and throws — not a real encoder failure. Since Stop() always claims
            // finalize *before* it cancels the pipe, losing this race reliably means someone else is already
            // driving a normal (or their own already-reported) finalize, so showing another Fatal toast here
            // would only be redundant or actively misleading. It already drives the state machine and raises
            // Finished; nothing more to do here.
            Log.Debug(ex, "Pacer loop's WriteFrame was cancelled during an already-in-progress Stop() (benign race, not a real failure).");
            return;
        }

        Log.Error(ex, "Recording pacer loop failed. ffmpeg stderr:\n{Stderr}", _session?.StandardError);
        _errors.Fatal("record.encoder-died", message, suggestion, ex);

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
        finally
        {
            _finalizationCompleted.Set();
        }
    }

    private RecordingResult Finalize()
    {
        // Stop the audio pump before the session closes the pipes.
        _audioStop?.Cancel();
        if (_audioThread is not null && _audioThread != Thread.CurrentThread)
        {
            _audioThread.Join();
        }

        string stderr = _session?.StandardError ?? "";
        // _lastRotatedSegmentResult covers the "Stop() raced a rotation's own finalize" case — see its doc
        // comment. Cleared here either way so a stale successful rotation from a PREVIOUS recording can never
        // leak into a later one's genuine "nothing to finalize" failure. usedStashedResult gates the
        // remux/library-add blocks below: when true, RotateSegment already did BOTH for this exact file
        // (_recordingPath/_finalPath still point at that same already-finalized, already-remuxed segment,
        // since the early return happens before they're advanced to the next one) — redoing either would try
        // to remux a file RotateSegment already deleted, and would double-add the library entry.
        bool usedStashedResult = _session is null && _lastRotatedSegmentResult is not null;
        RecordingResult result = _session?.StopAndFinalize(TimeSpan.FromSeconds(20))
            ?? _lastRotatedSegmentResult
            ?? new RecordingResult(false, -1, "", 0);
        _lastRotatedSegmentResult = null;

        if (!result.Success && stderr.Length > 0)
        {
            Log.Warning("ffmpeg stderr:\n{Stderr}", stderr);
        }

        // Snapshot-and-null under the same lock SetBrightness/SetZoomTarget/SetBaseRect/CaptureSupportsZoom
        // take around their entire _capture?.Xxx() call, so Stop()/Dispose() below can never run concurrently
        // with one of those still mid-call on the engine being torn down — see RetargetCapture's identical
        // pattern and _captureAccessLock's own doc comment for why.
        ICaptureEngine? capture;
        lock (_captureAccessLock)
        {
            capture = _capture;
            _capture = null;
        }
        capture?.Stop();
        _webcamCapture?.Stop();
        _session?.Dispose();
        if (capture is not null)
        {
            capture.Faulted -= OnCaptureFaulted;
        }
        capture?.Dispose();
        _mixer?.Dispose();
        _audioStop?.Dispose();
        _session = null;
        _webcamCapture = null;
        _mixer = null;
        _audioThread = null;
        _audioStop = null;
        _originalTarget = null;
        _pendingRetarget = null;

        // Safe recording: remux the crash-safe MKV to MP4 without re-encoding. Skipped when usedStashedResult
        // — RotateSegment already remuxed (and library-indexed) this exact file before stashing it.
        if (!usedStashedResult && result.Success && _safeRemux)
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

        if (!usedStashedResult && result.Success && result.OutputPath.Length > 0)
        {
            double duration = _metaFps > 0 ? (double)result.FramesWritten / _metaFps : 0;
            string directory = Path.GetDirectoryName(result.OutputPath) ?? string.Empty;
            _libraryIndex.Add(new RecMode.Core.Library.LibraryIndexEntry(
                Path.GetFileName(result.OutputPath), directory, _metaSource, _metaCodec, _metaContainer,
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
        (string finalPath, string recordingPath) = _safeRemux
            ? BuildSafeRecordingPaths(_outputDir, segFileName)
            : (FilenameBuilder.BuildUniquePath(_outputDir, segFileName), "");
        if (!_safeRemux)
        {
            recordingPath = finalPath;
        }
        return (recordingPath, finalPath);
    }

    /// <summary>Finds a final path whose paired safe-recording MKV is also unused. This prevents a new
    /// recording from overwriting a crash-recoverable <c>*.recording.mkv</c> before startup recovery reaches it.</summary>
    internal static (string FinalPath, string RecordingPath) BuildSafeRecordingPaths(string outputDir, string fileName)
    {
        string extension = Path.GetExtension(fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        int suffix = 0;
        while (true)
        {
            string name = suffix == 0 ? fileName : $"{stem} ({suffix}){extension}";
            string finalPath = Path.Combine(outputDir, name);
            string recordingPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(name) + ".recording.mkv");
            if (!File.Exists(finalPath) && !File.Exists(recordingPath))
            {
                return (finalPath, recordingPath);
            }

            suffix++;
        }
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
        // Captured before the finalize/remux/encoder-restart gap, not after: the pacer's Elapsed-driven
        // catch-up writes the new segment's early video frames back-to-back covering that whole gap the
        // instant it resumes, so the audio pump (started at the bottom of this method) must anchor its own
        // PTS=0 to this same pre-gap instant — see StartAudioPumpThread's doc comment.
        TimeSpan segmentStartedAt = _stateMachine.Elapsed;

        _audioStop?.Cancel();
        if (_audioThread is not null && _audioThread != Thread.CurrentThread)
        {
            _audioThread.Join();
        }
        _audioThread = null;

        string prevRecordingPath = _recordingPath;
        string prevFinalPath = _finalPath;
        string segmentStderr = _session?.StandardError ?? "";
        RecordingResult segResult = _session?.StopAndFinalize(TimeSpan.FromSeconds(20)) ?? new RecordingResult(false, -1, "", 0);
        _session?.Dispose();
        _session = null;

        if (!segResult.Success)
        {
            Log.Error("Segment finalization failed: {Path}; ffmpeg stderr:\n{Stderr}",
                prevRecordingPath, segmentStderr);
            _errors.Fatal("record.segment-finalize-failed",
                "A recording segment couldn't be finalized; recording was stopped to protect the remaining file.",
                "The partial segment was left on disk for recovery. See the log for encoder details.");
            _stopRequested = true;
            System.Threading.Tasks.Task.Run(Stop); // Stop joins this pacer thread, so never call it inline.
            return;
        }

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
            string directory = Path.GetDirectoryName(prevFinalPath) ?? string.Empty;
            _libraryIndex.Add(new RecMode.Core.Library.LibraryIndexEntry(
                Path.GetFileName(prevFinalPath), directory, _metaSource, _metaCodec, _metaContainer,
                _metaWidth, _metaHeight, _metaFps, duration, DateTimeOffset.Now,
                _metaQuality, _metaSystemAudioEnabled, _metaMicEnabled));

            // Stash in case a concurrent Stop() bails this method out at the _stopRequested check right
            // below — see the field's own doc comment.
            _lastRotatedSegmentResult = segResult with { OutputPath = prevFinalPath };
        }

        // Stop() (UI thread/tray/hotkey) can set this concurrently while the finalize/remux/library-write
        // above was in flight — that block reaches real wall-clock time (finalize alone waits up to 20s), and
        // this method has no other checkpoint against it. Without this, RotateSegment would go on to start a
        // brand-new encoder session for a segment that's about to be immediately abandoned: PaceLoop's next
        // iteration observes _stopRequested and exits before writing it a single frame, then Stop()'s own
        // Finalize() finalizes that near-empty session anyway — a spurious near-zero-frame extra file plus a
        // pointless safe-remux pass over it. Bailing here instead leaves _session null (already set above),
        // which Finalize() already handles cleanly (no file, no library entry, same as any other "nothing to
        // finalize" case) — the segments already rotated through above are entirely unaffected either way.
        if (_stopRequested)
        {
            return;
        }

        _segmentIndex++;
        (_recordingPath, _finalPath) = BuildSegmentPaths(_segmentIndex);
        // The new segment's file starts at 0 bytes and its own session starts at 0 FramesWritten — reset the
        // throughput sampler's baseline and the fps calc's segment-start anchor together, or the very next
        // RaiseProgress tick subtracts the PREVIOUS (now-larger) segment's size from the new (near-empty)
        // one's, producing a large negative Mbps reading for one tick after every rotation.
        _lastSizeBytes = 0;
        _lastSizeTicks = 0;
        _currentSegmentStartedAt = segmentStartedAt;

        var job = _jobTemplate! with
        {
            OutputPath = _recordingPath,
            PipeName = $"recmode_vid_{Guid.NewGuid():N}",
            AudioPipeName = _jobTemplate.AudioPipeName is null ? null : $"recmode_aud_{Guid.NewGuid():N}",
        };

        List<EncoderInfo> chain = forcedChain ?? _encoderChain!;
        _session = TryStartAnyEncoder(chain, job, _capture!.Nv12ByteSize);
        if (_session is null)
        {
            _errors.Fatal("record.split-failed", "Couldn't start the next recording segment; the recording was stopped.",
                "The previous segments are safe on disk.");
            // Same as the segment-finalize-failure branch above: without this, PaceLoop's next iteration
            // dereferences the now-null _session before Stop() (running on its own Task) gets a chance to
            // join and stop this pacer thread, throwing a second, spurious "stopped unexpectedly" failure
            // on top of this one.
            _stopRequested = true;
            System.Threading.Tasks.Task.Run(Stop); // Stop() joins the pacer thread, so never call it inline
            return;
        }

        // Stop() (UI thread/tray/hotkey) can also arrive during TryStartAnyEncoder itself — starting an
        // encoder tries each fallback candidate in turn and can legitimately take several seconds. The check
        // above only covers the finalize/remux/library-write gap; without this one too, a Stop() landing in
        // this narrower window still leaves a brand-new segment session open that will never receive a
        // frame, producing the same spurious zero-byte file the check above exists to prevent.
        if (_stopRequested)
        {
            _session.Dispose();
            TryDelete(_recordingPath);
            _session = null;
            return;
        }

        if (forcedChain is not null)
        {
            _encoderChain = forcedChain; // keep any later rotation (e.g. auto-split) on the downgraded encoder
        }

        if (_mixer is not null && _session.AudioPipe is { } audioPipe)
        {
            StartAudioPumpThread(audioPipe, segmentStartedAt);
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
    /// <summary>Returns the rotation's wall-clock duration only if a rotation to a software encoder actually
    /// happened; null otherwise. Callers must not treat a null return as "healthy again" — it means downgrade
    /// was already attempted this recording, the active encoder isn't hardware, or no software fallback
    /// exists for this codec/container, none of which changes whether the encoder is still falling behind.</summary>
    private TimeSpan? AttemptDowngrade()
    {
        if (_downgradeAttempted || _activeEncoder is not { IsHardware: true } activeEncoder)
        {
            return null;
        }

        _downgradeAttempted = true;
        List<EncoderInfo> swChain = _fallbackChain.BuildSoftwareOnly(activeEncoder, _jobTemplate!.Container);
        if (swChain.Count == 0)
        {
            return null; // no software encoder available for this codec — nothing to fall back to
        }

        _errors.Warn("record.encoder-downgrade",
            "Switching to software encoding — the hardware encoder couldn't keep up.",
            "This uses more CPU but should stay in sync with real time.");
        long rotationStart = Stopwatch.GetTimestamp();
        RotateSegment(swChain);
        return Stopwatch.GetElapsedTime(rotationStart);
    }

    /// <summary>Test-only seam (mirrors the temporary --selftest-* hooks): forces the hw→sw downgrade path
    /// deterministically instead of waiting for a genuine sustained encoder stall.</summary>
#if RECMODE_SELFTEST
    internal void TestForceDowngrade() => _testForceDowngrade = true;
#endif

    private FfmpegRecordingSession? TryStartAnyEncoder(List<EncoderInfo> chain, FfmpegJob template, int frameBytes)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            EncoderInfo enc = chain[i];
            var session = new FfmpegRecordingSession(_ffmpegPath!);
            // Ownership stays here until the session is successfully returned; the finally below disposes it
            // otherwise. FfmpegRecordingSession.Start creates BOTH named pipes before Process.Start, so ANY
            // throw past that point owns two live kernel pipe handles (and possibly a running ffmpeg child).
            // The catch filter below only covers encoder-shaped failures, so exceptions outside it — e.g.
            // Win32Exception from Process.Start when the binary is missing/AV-quarantined, or
            // UnauthorizedAccessException from NamedPipeServerStreamAcl.Create — used to escape with the
            // session never disposed, leaking those handles until finalization.
            bool keepSession = false;
            try
            {
                // Fresh pipe names per candidate, not just per rotation: a prior candidate's session that
                // timed out waiting for ffmpeg to connect (Start's own 8s bound) has already been Dispose()d,
                // but that Kill() doesn't wait for exit, so the OS pipe instance for its name may not be freed
                // yet — reusing the same name for the next candidate could then hit CreateNamedPipe's
                // "all pipe instances are busy" before ffmpeg even gets a chance to fail cleanly.
                FfmpegJob job = template with
                {
                    Encoder = enc,
                    PipeName = $"recmode_vid_{Guid.NewGuid():N}",
                    AudioPipeName = template.AudioPipeName is null ? null : $"recmode_aud_{Guid.NewGuid():N}",
                };
                session.Start(job, frameBytes);
                if (i > 0)
                {
                    _errors.Warn("record.encoder-fallback",
                        $"Using {enc.DisplayName} — the selected encoder wouldn't start.");
                }
                _activeEncoder = enc;
                keepSession = true;
                return session;
            }
            catch (Exception ex) when (ex is EncoderStartException or InvalidOperationException or IOException)
            {
                // Encoder-shaped failure: this candidate didn't work, try the next one. Anything OUTSIDE this
                // filter is deliberately left to propagate to Start()'s outer handler — a missing/blocked
                // ffmpeg binary is not encoder-specific, so walking the rest of the chain would just retry a
                // guaranteed failure and report the vaguer "no encoder could start" instead of the real
                // cause. Only the leak needed fixing here, not the propagation (see the finally).
                Log.Warning(ex, "Encoder {Enc} failed to start; trying next", enc.FfmpegId);
            }
            finally
            {
                if (!keepSession)
                {
                    session.Dispose();
                }
            }
        }

        return null;
    }

    private bool Remux(string mkvPath, string mp4Path) =>
        _ffmpegPath is not null && RecMode.Encoding.Ffmpeg.Remuxer.RemuxToMp4(_ffmpegPath, mkvPath, mp4Path, _activeEncoder?.Codec);

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
    // Elapsed-at-start of the CURRENT segment's file — TimeSpan.Zero for the first segment, reset in
    // RotateSegment for every one after. RaiseProgress's fps figure used to divide the new segment's own
    // FramesWritten (which restarts at 0 after every auto-split rotation) by the WHOLE recording's
    // cumulative Elapsed (which does not reset per segment) — so displayed fps collapsed toward zero for the
    // rest of a long auto-split recording after the first rotation, even though the encoder was running fine.
    private TimeSpan _currentSegmentStartedAt = TimeSpan.Zero;
    private int _targetFps;
    private volatile bool _encoderBehind; // health: the encoder can't keep up with real time
    private string? _outputRoot;          // drive root for the mid-recording disk-space guard
    private nint _zoomMonitorCacheHandle;
    private MonitorInfo? _zoomMonitorCache;

    private void RaiseProgress()
    {
        // Read _session into a local exactly once: this is a genuine cross-thread field (RotateSegment, on
        // the pacer thread, nulls it out for the whole finalize+remux+restart window), and the 0.9.64 "fix"
        // of guarding on `_session is null` still re-read the field a second time for the dereference below
        // — with _stateMachine.Elapsed's internal lock acquisition sitting in between, nothing prevented the
        // JIT/another thread from observing a different value on the second read. Snapshotting into `session`
        // once makes the null-check and the use refer to the same object no matter what other threads do.
        FfmpegRecordingSession? session = _session;
        double segmentElapsedSeconds = (_stateMachine.Elapsed - _currentSegmentStartedAt).TotalSeconds;
        double fps = session is null || segmentElapsedSeconds < 0.1
            ? 0
            : session.FramesWritten / segmentElapsedSeconds;

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
                        // Clamped to non-negative as a last-resort safety net alongside the reset above — a
                        // real file size never legitimately shrinks between samples.
                        mbps = Math.Max(0, (size - _lastSizeBytes) * 8 / dt / 1_000_000.0);
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
            _stateMachine.State, _stateMachine.Elapsed, fps, session?.FramesWritten ?? 0, mbps, size,
            IsHealthy: !_encoderBehind));
    }

    private void SafeTeardown()
    {
        // Mirrors Stop()'s own ordering (cancel the pipe write, signal the pacer, THEN wait for it) before
        // touching anything the pacer might still be using. Without this, an exception thrown between the
        // pacer thread's launch and the end of Start() (e.g. ClearBuffers/StartAudioPumpThread failing) would
        // reach this method while the pacer thread was still alive on _capture/_session — disposing live
        // D3D11/COM/pipe objects out from under a thread actively calling into them, which is a genuine
        // access-violation risk, not just a managed NRE.
        _stopRequested = true;
        try { _session?.RequestStop(); } catch (ObjectDisposedException) { }
        if (_pacer is not null && _pacer != Thread.CurrentThread)
        {
            _pacer.Join();
        }
        _pacer = null;

        try { _audioStop?.Cancel(); } catch (Exception) { }
        bool audioThreadStopped = true;
        try { audioThreadStopped = _audioThread is null || _audioThread.Join(1000); } catch (Exception) { }
        try { _session?.Dispose(); } catch (Exception) { }
        try { if (_capture is not null) { _capture.Faulted -= OnCaptureFaulted; } } catch (Exception) { }
        try { _capture?.Dispose(); } catch (Exception) { }
        try { _webcamCapture?.Stop(); } catch (Exception) { }
        if (audioThreadStopped)
        {
            try { _mixer?.Dispose(); } catch (Exception) { }
        }
        else if (_mixer is not null && _audioThread is not null)
        {
            // The audio thread didn't stop within the bounded join above — it may still be inside a
            // blocking WASAPI read using _mixer. Disposing here would race that in-flight call (exactly the
            // ordering bug this branch exists to avoid, since the join is deliberately bounded rather than
            // unbounded — this runs on the UI thread during a failed Start()'s cleanup, which must not hang
            // indefinitely). But simply leaving it undisposed (the previous behavior) orphaned it forever:
            // the field below still gets nulled unconditionally, so nothing would ever call Dispose() on it
            // even once the thread eventually did finish — a live WASAPI capture client (and the OS
            // "microphone in use" indicator) leaked for the rest of the process's lifetime. Instead, hand
            // the still-referenced thread and mixer to a background task that finishes the join (unbounded
            // is fine off the UI thread) and disposes the mixer once that actually completes.
            Log.Warning("SafeTeardown: audio thread didn't stop within 1s; deferring mixer disposal until it actually exits");
            Thread orphanedThread = _audioThread;
            RecMode.Audio.IAudioMixer orphanedMixer = _mixer;
            System.Threading.Tasks.Task.Run(() =>
            {
                orphanedThread.Join();
                try { orphanedMixer.Dispose(); }
                catch (Exception ex) { Log.Warning(ex, "Deferred mixer disposal after a stuck audio thread failed"); }
            });
        }
        try { _audioStop?.Dispose(); } catch (Exception) { }
        _session = null;
        _capture = null;
        _webcamCapture = null;
        _mixer = null;
        _audioThread = null;
        _audioStop = null;
        _originalTarget = null;
        _pendingRetarget = null;
    }

    internal static string ContainerExtension(MediaContainer c) => c switch
    {
        MediaContainer.Mp4 => "mp4",
        MediaContainer.Mkv => "mkv",
        MediaContainer.Mov => "mov",
        MediaContainer.WebM => "webm",
        _ => "mp4",
    };

    public void Dispose()
    {
        SafeTeardown();
        // Only disposed here, in the real object-lifetime Dispose() — never inside SafeTeardown() itself,
        // which also runs on ordinary failed-Start() cleanup paths while the coordinator (a DI singleton)
        // is still very much alive and needs _finalizationCompleted to keep working for the rest of the
        // session (Wait()/Set()/Reset() all throw ObjectDisposedException after this).
        _finalizationCompleted.Dispose();
    }
}
