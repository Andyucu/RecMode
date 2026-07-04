using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace RecMode.Spike;

/// <summary>
/// The full Tier-1 spike: WGC capture → GPU NV12 → readback → named pipe → ffmpeg → file, paced to CFR,
/// optionally muxing a second WASAPI-loopback audio pipe. Measures captured/paced frame counts, pipe-write
/// stalls, throughput, and this process's CPU (ffmpeg runs separately, so our CPU excludes it — exactly
/// what the §3.3 gate wants). Disposable spike code.
/// </summary>
internal sealed class Recorder
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    private readonly string _ffmpegPath;
    private readonly int _fps;

    private readonly Lock _sync = new();
    private byte[] _latest = [];
    private bool _hasLatest;

    private long _capturedFrames;
    private long _convertedFrames;
    private long _pacedFrames;
    private long _bytesWritten;
    private long _pipeStalls;
    private long _maxWriteMicros;
    private long _audioBytes;

    public Recorder(string ffmpegPath, int fps)
    {
        _ffmpegPath = ffmpegPath;
        _fps = fps;
    }

    public async Task<int> RunAsync(int seconds, string encoder, string container, int dstW, int dstH, bool withAudio)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            Console.Error.WriteLine("WGC not supported.");
            return 2;
        }

        (ID3D11Device device, ID3D11DeviceContext context, _) = Interop.CreateDevice();
        using (device)
        using (context)
        {
            IDirect3DDevice winrt = Interop.CreateWinRtDevice(device);
            GraphicsCaptureItem item = Interop.CreateItemForPrimaryMonitor();
            int srcW = item.Size.Width, srcH = item.Size.Height;
            Console.WriteLine($"[cap] source {srcW}x{srcH} -> output {dstW}x{dstH} @ {_fps}fps, {encoder} -> {container}, audio={withAudio}");

            using var converter = new Nv12Converter(device, context, srcW, srcH, dstW, dstH);
            int frameBytes = converter.Nv12ByteSize;
            _latest = new byte[frameBytes];
            byte[] convertScratch = new byte[frameBytes];

            string outPath = Path.Combine(Path.GetTempPath(), $"recmode-spike.{container}");
            if (File.Exists(outPath)) File.Delete(outPath);
            string vidPipeName = $"recmode_vid_{Environment.ProcessId}";
            string audPipeName = $"recmode_aud_{Environment.ProcessId}";

            AudioLoopback? audio = withAudio ? new AudioLoopback() : null;
            using var _ = audio;

            var vidPipe = new NamedPipeServerStream(
                vidPipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, frameBytes * 4, frameBytes * 4);
            NamedPipeServerStream? audPipe = withAudio
                ? new NamedPipeServerStream(audPipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 1 << 20, 1 << 20)
                : null;

            using Process ffmpeg = StartFfmpeg(vidPipeName, audPipeName, encoder, container, dstW, dstH, outPath, audio);

            Console.WriteLine("[pipe] waiting for ffmpeg to connect video...");
            await vidPipe.WaitForConnectionAsync();
            Console.WriteLine("[pipe] video connected.");

            // ffmpeg won't open the audio pipe until it has probed the video stream, and it can't probe
            // until video bytes flow. So the audio pipe's connection wait + pump live entirely on their own
            // thread, and video pacing starts immediately below — otherwise the two inputs deadlock.
            using var audioStop = new CancellationTokenSource();
            Thread? audioThread = null;
            if (audio is not null && audPipe is not null)
            {
                audioThread = new Thread(() =>
                {
                    try
                    {
                        audPipe.WaitForConnection();
                        Console.WriteLine("[pipe] audio connected.");
                        audio.Start();
                        _audioBytes = audio.PumpUntil(audPipe, audioStop.Token);
                    }
                    catch (Exception ex) { Console.WriteLine($"[audio] {ex.GetType().Name}: {ex.Message}"); }
                }) { IsBackground = true, Name = "audio-pump" };
                audioThread.Start();
            }

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            framePool.FrameArrived += (pool, _) =>
            {
                using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
                if (frame is null) return;
                Interlocked.Increment(ref _capturedFrames);
                using ID3D11Texture2D tex = Interop.GetTexture(frame.Surface);
                converter.Convert(tex, convertScratch);
                lock (_sync)
                {
                    (convertScratch, _latest) = (_latest, convertScratch);
                    _hasLatest = true;
                }
                Interlocked.Increment(ref _convertedFrames);
            };

            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            session.StartCapture();

            await PaceAndWriteAsync(vidPipe, seconds, frameBytes);

            wall.Stop();
            session.Dispose();
            var cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;

            // Stop audio, then signal clean EOF on both pipes.
            audioStop.Cancel();
            audioThread?.Join(2000);

            vidPipe.Flush();
            try { vidPipe.WaitForPipeDrain(); } catch (IOException) { }
            vidPipe.Dispose();
            if (audPipe is not null)
            {
                try { audPipe.Flush(); audPipe.WaitForPipeDrain(); } catch (IOException) { }
                audPipe.Dispose();
            }
            Console.WriteLine("[pipe] closed; waiting for ffmpeg to finalize...");
            ffmpeg.WaitForExit(20000);

            ReportMeasurements(wall.Elapsed, cpuAfter - cpuBefore, frameBytes, seconds, outPath, ffmpeg.ExitCode, withAudio, audio);
            return 0;
        }
    }

    private async Task PaceAndWriteAsync(NamedPipeServerStream pipe, int seconds, int frameBytes)
    {
        byte[] writeBuf = new byte[frameBytes];
        long interval = Stopwatch.Frequency / _fps;
        long start = Stopwatch.GetTimestamp();
        long totalTicks = (long)seconds * Stopwatch.Frequency;

        _ = timeBeginPeriod(1);
        try
        {
            for (long i = 0; ; i++)
            {
                long target = start + i * interval;
                if (target - start > totalTicks) break;
                WaitUntil(target);

                bool ready;
                lock (_sync)
                {
                    ready = _hasLatest;
                    if (ready) Buffer.BlockCopy(_latest, 0, writeBuf, 0, frameBytes);
                }
                if (!ready) continue;

                long t0 = Stopwatch.GetTimestamp();
                await pipe.WriteAsync(writeBuf.AsMemory(0, frameBytes));
                long micros = (Stopwatch.GetTimestamp() - t0) * 1_000_000 / Stopwatch.Frequency;
                if (micros > _maxWriteMicros) _maxWriteMicros = micros;
                if (micros > 1_000_000 / _fps) Interlocked.Increment(ref _pipeStalls);

                Interlocked.Increment(ref _pacedFrames);
                _bytesWritten += frameBytes;
            }
        }
        finally
        {
            _ = timeEndPeriod(1);
        }
    }

    private static void WaitUntil(long targetTicks)
    {
        while (true)
        {
            long now = Stopwatch.GetTimestamp();
            long remaining = targetTicks - now;
            if (remaining <= 0) return;
            double ms = remaining * 1000.0 / Stopwatch.Frequency;
            if (ms > 2) Thread.Sleep(1);
            else Thread.SpinWait(64);
        }
    }

    private Process StartFfmpeg(string vidPipe, string audPipe, string encoder, string container,
        int w, int h, string outPath, AudioLoopback? audio)
    {
        string enc = encoder switch
        {
            "h264_amf" => "-c:v h264_amf -usage transcoding -quality balanced -rc cqp -qp_i 22 -qp_p 22",
            "libx264" => "-c:v libx264 -preset veryfast -crf 23",
            _ => throw new ArgumentException($"unknown encoder {encoder}"),
        };
        string mov = container == "mp4" ? "-movflags +faststart" : "";

        string videoIn = $"-f rawvideo -pix_fmt nv12 -s {w}x{h} -r {_fps} -i \\\\.\\pipe\\{vidPipe}";
        string audioIn = "", audioMap = "", audioEnc = "";
        if (audio is not null)
        {
            audioIn = $"-f f32le -ar {audio.SampleRate} -ac {audio.Channels} -i \\\\.\\pipe\\{audPipe}";
            audioMap = "-map 0:v:0 -map 1:a:0";
            audioEnc = "-c:a aac -b:a 192k";
        }

        string args =
            $"-hide_banner -loglevel warning {videoIn} {audioIn} {audioMap} {enc} -pix_fmt yuv420p {audioEnc} {mov} -y \"{outPath}\"";

        Console.WriteLine($"[ffmpeg] {args}");
        var psi = new ProcessStartInfo(_ffmpegPath, args) { UseShellExecute = false };
        return Process.Start(psi)!;
    }

    private void ReportMeasurements(TimeSpan wall, TimeSpan cpu, int frameBytes, int seconds,
        string outPath, int ffmpegExit, bool withAudio, AudioLoopback? audio)
    {
        int cores = Environment.ProcessorCount;
        double cpuPctOneCore = cpu.TotalMilliseconds / wall.TotalMilliseconds * 100.0;
        double cpuPctAllCores = cpuPctOneCore / cores;
        double mbPerSec = _bytesWritten / (1024.0 * 1024.0) / wall.TotalSeconds;
        long expectedPaced = (long)seconds * _fps;

        Console.WriteLine();
        Console.WriteLine("================ SPIKE MEASUREMENTS ================");
        Console.WriteLine($"wall time            : {wall.TotalSeconds:F2}s");
        Console.WriteLine($"captured frames (WGC): {_capturedFrames}  ({_capturedFrames / wall.TotalSeconds:F1}/s delivered)");
        Console.WriteLine($"converted frames     : {_convertedFrames}");
        Console.WriteLine($"paced frames (CFR)   : {_pacedFrames} / {expectedPaced} expected");
        Console.WriteLine($"pipe throughput      : {mbPerSec:F1} MB/s  ({_bytesWritten / (1024 * 1024)} MB total, {frameBytes / 1024} KB/frame)");
        Console.WriteLine($"pipe stalls (>frame) : {_pipeStalls}  (max write {_maxWriteMicros} us)");
        Console.WriteLine($"app CPU (excl ffmpeg): {cpuPctOneCore:F1}% of one core  ({cpuPctAllCores:F1}% of all {cores} cores)");
        if (withAudio && audio is not null)
        {
            double audioSec = _audioBytes / (double)audio.BytesPerSecond;
            Console.WriteLine($"audio                : {audio.SampleRate}Hz {audio.Channels}ch f32le, {_audioBytes / 1024} KB (~{audioSec:F2}s)");
        }
        Console.WriteLine($"peak working set     : {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MB");
        Console.WriteLine($"ffmpeg exit code     : {ffmpegExit}");
        if (File.Exists(outPath))
        {
            Console.WriteLine($"output file          : {outPath}  ({new FileInfo(outPath).Length / (1024 * 1024)} MB)");
        }
        Console.WriteLine("====================================================");
        Console.WriteLine($"OUTPUT_PATH={outPath}");
    }
}
