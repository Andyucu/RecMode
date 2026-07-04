using System.Diagnostics;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace RecMode.Spike;

internal static class Program
{
    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "probe";
        try
        {
            return mode switch
            {
                "probe" => Probe(seconds: args.Length > 1 ? int.Parse(args[1]) : 3),
                "record" => Record(args, withAudio: false),
                "recordav" => Record(args, withAudio: true),
                _ => Fail($"unknown mode '{mode}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[fatal] {ex}");
            return 1;
        }
    }

    /// <summary>record[av] &lt;seconds&gt; &lt;h264_amf|libx264&gt; &lt;mp4|mkv&gt; [WxH|native]</summary>
    private static int Record(string[] args, bool withAudio)
    {
        int seconds = args.Length > 1 ? int.Parse(args[1]) : 10;
        string encoder = args.Length > 2 ? args[2] : "h264_amf";
        string container = args.Length > 3 ? args[3] : "mp4";
        int dstW = 2560, dstH = 1440; // gate parity with the plan's 1440p60 benchmark
        if (args.Length > 4 && args[4] != "native")
        {
            string[] wh = args[4].Split('x');
            dstW = int.Parse(wh[0]);
            dstH = int.Parse(wh[1]);
        }

        string ffmpeg = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "ffmpeg", "ffmpeg.exe");
        ffmpeg = Path.GetFullPath(ffmpeg);
        if (!File.Exists(ffmpeg))
        {
            return Fail($"ffmpeg not found at {ffmpeg}");
        }

        if (args.Length > 4 && args[4] == "native")
        {
            // Resolve native size at runtime from the capture item.
            dstW = 0; dstH = 0;
        }

        var recorder = new Recorder(ffmpeg, fps: 60);
        if (dstW == 0)
        {
            Windows.Graphics.Capture.GraphicsCaptureItem item = Interop.CreateItemForPrimaryMonitor();
            dstW = item.Size.Width;
            dstH = item.Size.Height;
        }
        return recorder.RunAsync(seconds, encoder, container, dstW, dstH, withAudio).GetAwaiter().GetResult();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"[error] {message}");
        return 2;
    }

    /// <summary>Confirms WGC frames actually arrive in this environment and reports the delivered fps.</summary>
    private static int Probe(int seconds)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            return Fail("Windows.Graphics.Capture is not supported on this OS.");
        }

        (ID3D11Device device, ID3D11DeviceContext context, _) = Interop.CreateDevice();
        using (device)
        using (context)
        {
            IDirect3DDevice winrt = Interop.CreateWinRtDevice(device);
            GraphicsCaptureItem item = Interop.CreateItemForPrimaryMonitor();
            Console.WriteLine($"[wgc] item = {item.DisplayName}  size = {item.Size.Width}x{item.Size.Height}");

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winrt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);

            int frames = 0;
            long firstTicks = 0, lastTicks = 0;
            framePool.FrameArrived += (pool, _) =>
            {
                using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
                if (frame is null)
                {
                    return;
                }

                long now = Stopwatch.GetTimestamp();
                if (frames == 0) firstTicks = now;
                lastTicks = now;
                Interlocked.Increment(ref frames);
            };

            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            session.StartCapture();
            Console.WriteLine($"[wgc] capturing for {seconds}s...");
            Thread.Sleep(seconds * 1000);

            double span = firstTicks == 0 ? 0 : (lastTicks - firstTicks) / (double)Stopwatch.Frequency;
            double fps = span > 0 ? (frames - 1) / span : 0;
            Console.WriteLine($"[wgc] frames = {frames}  span = {span:F2}s  delivered fps = {fps:F1}");
            return frames > 0 ? 0 : Fail("no frames arrived");
        }
    }
}
