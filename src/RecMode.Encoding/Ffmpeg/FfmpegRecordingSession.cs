using System.Diagnostics;
using System.IO.Pipes;

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
    private readonly string _ffmpegPath;
    private readonly System.Text.StringBuilder _stderr = new();
    private readonly Lock _stderrLock = new();
    private NamedPipeServerStream? _pipe;
    private Process? _ffmpeg;
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

        _pipe = new NamedPipeServerStream(
            job.PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, frameBytes * 4, frameBytes * 4);

        if (job.AudioPipeName is not null)
        {
            AudioPipe = new NamedPipeServerStream(
                job.AudioPipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 1 << 20, 1 << 20);
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

        // Capture ffmpeg's stderr so failures are diagnosable (logged on finalize / pipe break).
        _ffmpeg.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (_stderrLock) { _stderr.AppendLine(e.Data); } } };
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
            _pipe.Write(frame, 0, length);
            _framesWritten++;
        }
        catch (IOException ex)
        {
            throw new EncoderPipeBrokenException("The encoder pipe broke (ffmpeg exited unexpectedly).", ex);
        }
    }

    /// <summary>Signals clean EOF, waits for ffmpeg to finalize the container, and returns the result.</summary>
    public RecordingResult StopAndFinalize(TimeSpan timeout)
    {
        if (_pipe is not null)
        {
            try
            {
                _pipe.Flush();
                _pipe.WaitForPipeDrain();
            }
            catch (IOException)
            {
                // ffmpeg may already be closing; EOF still propagates on dispose.
            }

            _pipe.Dispose();
            _pipe = null;
        }

        if (AudioPipe is not null)
        {
            try { AudioPipe.Flush(); AudioPipe.WaitForPipeDrain(); } catch (IOException) { }
            AudioPipe.Dispose();
            AudioPipe = null;
        }

        int exitCode = -1;
        if (_ffmpeg is not null)
        {
            if (_ffmpeg.WaitForExit((int)timeout.TotalMilliseconds))
            {
                exitCode = _ffmpeg.ExitCode;
            }
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
    }
}
