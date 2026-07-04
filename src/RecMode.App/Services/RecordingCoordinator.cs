using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

    public RecordingCoordinator(
        Func<ICaptureEngine> captureFactory,
        IFfmpegLocator ffmpeg,
        IAppPaths paths,
        ISettingsService settings,
        IErrorReporter errors,
        RecordingStateMachine stateMachine)
    {
        _captureFactory = captureFactory;
        _ffmpeg = ffmpeg;
        _paths = paths;
        _settings = settings;
        _errors = errors;
        _stateMachine = stateMachine;
    }

    public RecordingState State => _stateMachine.State;
    public bool IsRecording => _stateMachine.IsActive;

    /// <summary>Throttled progress (≤ 4 Hz). Raised on the pacing thread — the VM marshals to the dispatcher.</summary>
    public event Action<RecordingProgress>? ProgressChanged;

    /// <summary>Raised on stop/finalize with the outcome. Also raised (Success=false) on a fatal mid-recording failure.</summary>
    public event Action<RecordingResult>? Finished;

    /// <summary>Starts a recording. Returns false (with a reported BlockingError) if pre-flight fails.</summary>
    public bool Start(MonitorInfo monitor, EncoderInfo encoder, MediaContainer container, int fps, int quality)
    {
        if (_stateMachine.IsActive)
        {
            return false;
        }

        // --- Pre-flight (§3.6) ---
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

        try
        {
            (int dstW, int dstH) = CaptureSizing.Resolve(monitor.Width, monitor.Height, encoder);

            _capture = _captureFactory();
            _capture.Start(monitor, dstW, dstH);

            string ext = ContainerExtension(container);
            string fileName = FilenameBuilder.BuildFileName(
                _settings.Current.FilenamePattern, DateTimeOffset.Now, "Display", encoder.Codec.ToString(), ext);
            string outputPath = FilenameBuilder.BuildUniquePath(outputDir, fileName);

            var job = new FfmpegJob
            {
                Encoder = encoder,
                Container = container,
                Width = dstW,
                Height = dstH,
                FrameRate = fps,
                Quality = quality,
                PipeName = $"recmode_vid_{Environment.ProcessId}_{Environment.TickCount}",
                OutputPath = outputPath,
            };

            _session = new FfmpegRecordingSession(ff.FfmpegPath);
            _session.Start(job, _capture.Nv12ByteSize);

            _stateMachine.StartRecording();
            _stopRequested = false;
            _pacer = new Thread(() => PaceLoop(fps)) { IsBackground = true, Name = "recmode-pacer" };
            _pacer.Start();

            Log.Information("Recording started: {Enc} {W}x{H}@{Fps} -> {Path}", encoder.FfmpegId, dstW, dstH, fps, outputPath);
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

    private void PaceLoop(int fps)
    {
        byte[] frame = new byte[_capture!.Nv12ByteSize];
        long interval = Stopwatch.Frequency / fps;
        long start = Stopwatch.GetTimestamp();
        long lastReport = start;

        _ = timeBeginPeriod(1);
        try
        {
            for (long i = 0; !_stopRequested; i++)
            {
                long target = start + i * interval;
                WaitUntil(target, () => _stopRequested);
                if (_stopRequested)
                {
                    break;
                }

                if (!_capture.TryGetLatestFrame(frame))
                {
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

                long now = Stopwatch.GetTimestamp();
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
        _session = null;
        _capture = null;

        Log.Information("Recording finalized: success={Success} frames={Frames} -> {Path}",
            result.Success, result.FramesWritten, result.OutputPath);
        return result;
    }

    private void RaiseProgress()
    {
        double fps = _capture is null || _stateMachine.Elapsed.TotalSeconds < 0.1
            ? 0
            : _session!.FramesWritten / _stateMachine.Elapsed.TotalSeconds;
        ProgressChanged?.Invoke(new RecordingProgress(
            _stateMachine.State, _stateMachine.Elapsed, fps, _session?.FramesWritten ?? 0));
    }

    private static void WaitUntil(long targetTicks, Func<bool> abort)
    {
        while (!abort())
        {
            long remaining = targetTicks - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return;
            }

            double ms = remaining * 1000.0 / Stopwatch.Frequency;
            if (ms > 2) Thread.Sleep(1);
            else Thread.SpinWait(64);
        }
    }

    private void SafeTeardown()
    {
        try { _session?.Dispose(); } catch (Exception) { }
        try { _capture?.Dispose(); } catch (Exception) { }
        _session = null;
        _capture = null;
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
