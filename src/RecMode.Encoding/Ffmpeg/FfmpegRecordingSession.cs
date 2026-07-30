using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Serilog;

namespace RecMode.Encoding.Ffmpeg;

/// <summary>Thrown when the ffmpeg pipe breaks mid-write (ffmpeg died). Maps to a FatalFinalizationError.</summary>
public sealed class EncoderPipeBrokenException(string message, Exception inner) : Exception(message, inner);

/// <summary>Thrown when ffmpeg fails to start or connect for an encoder (drives the fallback chain, §3.6).</summary>
public sealed class EncoderStartException(string message) : Exception(message);

/// <summary>Result of finalizing a recording session.</summary>
public sealed record RecordingResult(bool Success, int ExitCode, string OutputPath, long FramesWritten);

/// <summary>
/// Owns one ffmpeg subprocess and the named pipe feeding it NV12 frames (the Tier-1 path proven in the
/// Phase 0.5 spike). Not thread-safe: one producer calls <see cref="WriteFrame"/> in sequence.
/// </summary>
public sealed class FfmpegRecordingSession : IDisposable
{
    // Bounds a chatty encoder's stderr over a long recording — nothing here previously capped it, and
    // `-loglevel warning` is not silent: a per-frame condition (a driver warning, a "non monotonically
    // increasing dts" notice) can emit one line per frame, and at 60fps over 2 hours that's 432,000 lines —
    // tens of MB in a StringBuilder that then gets .ToString()'d (allocating another full copy) on every
    // Finalize/RotateSegment/pipe-break, directly against the "memory flat over 2h" budget. Only read on
    // failure, so keeping just the tail is enough to diagnose what actually went wrong.
    private const int MaxStderrChars = 262_144; // ~256 KB

    private readonly string _ffmpegPath;
    private readonly System.Text.StringBuilder _stderr = new();
    private readonly Lock _stderrLock = new();
    private NamedPipeServerStream? _pipe;
    private Process? _ffmpeg;
    private readonly CancellationTokenSource _writeCancellation = new();
    private long _framesWritten;
    private bool _disposed;

    public string OutputPath { get; private set; } = "";
    public long FramesWritten => _framesWritten;

    /// <summary>The audio input pipe (server side) when the job configured audio; the coordinator connects + pumps it.</summary>
    public NamedPipeServerStream? AudioPipe { get; private set; }

    /// <summary>ffmpeg's captured stderr (for diagnosing failures).</summary>
    public string StandardError { get { lock (_stderrLock) { return _stderr.ToString(); } } }

    public FfmpegRecordingSession(string ffmpegPath) => _ffmpegPath = ffmpegPath;

    /// <summary>Starts ffmpeg and waits for it to connect to the pipe. Throws if ffmpeg fails to start.</summary>
    public void Start(FfmpegJob job, int frameBytes)
    {
        ArgumentNullException.ThrowIfNull(job);
        OutputPath = job.OutputPath;

        _pipe = CreateSecurePipe(job.PipeName, frameBytes * 4);

        if (job.AudioPipeName is not null)
        {
            AudioPipe = CreateSecurePipe(job.AudioPipeName, 1 << 20);
        }

        string args = FfmpegArgsBuilder.Build(job);
        var psi = new ProcessStartInfo(_ffmpegPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        _ffmpeg = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        if (job.BelowNormalPriority)
        {
            // Best-effort: keep the encoder from starving foreground work (§3.3). Never fatal if it fails.
            try { _ffmpeg.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (Exception ex) { Log.Debug(ex, "Couldn't lower the ffmpeg process priority"); }
        }

        // Capture ffmpeg's stderr so failures are diagnosable (logged on finalize / pipe break).
        _ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (_stderrLock)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > MaxStderrChars)
                {
                    _stderr.Remove(0, _stderr.Length - MaxStderrChars);
                }
            }
        };
        _ffmpeg.BeginErrorReadLine();

        // ffmpeg opens the pipe as it initializes; wait for it to connect, but bail if it exits first or
        // never connects (a bad encoder). Otherwise WaitForConnection would deadlock.
        System.Threading.Tasks.Task connect = _pipe.WaitForConnectionAsync();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!connect.IsCompleted)
        {
            if (_ffmpeg.HasExited)
            {
                throw new EncoderStartException($"ffmpeg exited (code {_ffmpeg.ExitCode}) before the encoder pipe connected.");
            }

            if (timer.Elapsed > TimeSpan.FromSeconds(8))
            {
                throw new EncoderStartException("ffmpeg didn't connect to the encoder pipe within 8 seconds.");
            }

            System.Threading.Thread.Sleep(30);
        }
    }

    /// <summary>Writes one tightly-packed NV12 frame. Throws <see cref="EncoderPipeBrokenException"/> if ffmpeg died.</summary>
    public void WriteFrame(byte[] frame, int length)
    {
        if (_pipe is null)
        {
            throw new InvalidOperationException("Session not started.");
        }

        try
        {
            _pipe.WriteAsync(frame.AsMemory(0, length), _writeCancellation.Token).AsTask().GetAwaiter().GetResult();
            _framesWritten++;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            throw new EncoderPipeBrokenException("The encoder pipe broke (ffmpeg exited unexpectedly).", ex);
        }
    }

    /// <summary>Creates a named pipe restricted to the current Windows user. The plain
    /// <c>NamedPipeServerStream</c> constructor (this file's previous approach) passes a null security
    /// descriptor to <c>CreateNamedPipe</c>, and Windows' default DACL for that case grants read access to
    /// <c>Everyone</c> — and these pipes carry raw NV12 desktop frames and raw system/mic audio. Pipe names
    /// are predictable (<c>recmode_vid_&lt;pid&gt;_&lt;tickcount&gt;</c>) and <c>\\.\pipe\</c> is an
    /// enumerable directory, so any other local user account on the machine could open and read the pipe —
    /// and since it's created before <c>ffmpeg.exe</c> is started (a few instances/thread-creation calls
    /// away from actually connecting), a process spinning on <c>CreateFile</c> reliably wins that race.
    /// Restricting the pipe's DACL to only the identity that created it closes that off entirely: a
    /// different account's <c>CreateFile</c> now fails with access denied regardless of timing.</summary>
    private static NamedPipeServerStream CreateSecurePipe(string pipeName, int bufferSize)
    {
        var security = new PipeSecurity();
        SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Couldn't resolve the current Windows user's SID.");
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            bufferSize, bufferSize, security);
    }

    /// <summary>Cancels a producer currently blocked on the video pipe without tearing down session resources.</summary>
    public void RequestStop() => _writeCancellation.Cancel();

    /// <summary>Signals clean EOF, waits for ffmpeg to finalize the container, and returns the result.
    /// <paramref name="stallTimeout"/> is a no-progress window, not a total budget — see
    /// <see cref="FfmpegProcessWait"/> for why a fixed total budget used to destroy large recordings by
    /// killing ffmpeg mid-<c>+faststart</c> rewrite.</summary>
    public RecordingResult StopAndFinalize(TimeSpan stallTimeout)
    {
        RequestStop();

        if (_pipe is not null)
        {
            try
            {
                // Closing the server is the EOF signal ffmpeg needs. Do not wait for a pipe drain here:
                // it has no timeout and can deadlock forever when ffmpeg or a network output stalls.
                _pipe.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // ffmpeg may already be closing.
            }
            _pipe = null;
        }

        if (AudioPipe is not null)
        {
            try { AudioPipe.Dispose(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            AudioPipe = null;
        }

        int exitCode = -1;
        if (_ffmpeg is not null)
        {
            FfmpegProcessWait.WaitWithStallDetection(_ffmpeg, OutputPath, stallTimeout, out exitCode);
        }

        bool success = exitCode == 0 && File.Exists(OutputPath);
        return new RecordingResult(success, exitCode, OutputPath, _framesWritten);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeCancellation.Cancel();
        try { _pipe?.Dispose(); } catch (IOException) { }
        try { AudioPipe?.Dispose(); } catch (IOException) { }

        try
        {
            if (_ffmpeg is not null && !_ffmpeg.HasExited)
            {
                _ffmpeg.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }

        _ffmpeg?.Dispose();
        _writeCancellation.Dispose();
    }
}
