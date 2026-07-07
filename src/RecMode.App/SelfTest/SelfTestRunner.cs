using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RecMode.App.Services;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using Serilog;

namespace RecMode.App.SelfTest;

/// <summary>
/// Headless verification hooks driven by the <c>--selftest-*</c> CLI switches (temporary scaffolding predating
/// the Phase 5 CLI; kept because it's still the fastest way to exercise the real production pipeline —
/// capture → encode → mux — end to end without a physical test harness). Runs inside the live app process
/// (real DI container, real WGC capture, real ffmpeg subprocess), which is why this lives in RecMode.App
/// rather than a conventional test project: it needs the same STA/WPF message pump and fully-wired host that
/// <see cref="App"/> itself boots, not an isolated unit-test runner. Extracted out of <see cref="App"/> so
/// production startup wiring and this test-only scaffolding aren't interleaved in the same file.
/// </summary>
internal sealed class SelfTestRunner(IHost host, IAppPaths paths, Dispatcher dispatcher, Action<int> shutdown)
{
    public void Run(string mode)
    {
        bool region = mode == "region";
        string result = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");

        // Overlay verification: capture the countdown + toolbar via the WGC path with and without capture
        // exclusion, to prove both that they render and that exclusion keeps them out of the recording.
        if (mode == "overlays")
        {
            _ = RunOverlaySelfTestAsync();
            return;
        }

        // Click-ripple verification: show a ripple and WGC-capture it (not excluded → part of the recording).
        if (mode == "ripple")
        {
            _ = RunRippleSelfTestAsync();
            return;
        }

        // Annotation verification: draw a stroke on the overlay and WGC-capture it (not excluded).
        if (mode == "annotate")
        {
            _ = RunAnnotateSelfTestAsync();
            return;
        }

        // Screenshot runs synchronously on this (UI) thread — the clipboard copy needs STA.
        if (mode == "screenshot")
        {
            var svc = host.Services.GetRequiredService<ScreenshotService>();
            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            string? path = svc.Capture(RecMode.Capture.CaptureTarget.FromMonitor(mon));
            System.IO.File.WriteAllText(result, $"success={path is not null}\npath={path}\n");
            dispatcher.BeginInvoke(() => shutdown(path is null ? 3 : 0));
            return;
        }

        // "av" mode: force system audio on so the recording gets an audio track.
        if (mode == "av")
        {
            var s = host.Services.GetRequiredService<ISettingsService>();
            s.Current.SystemAudioEnabled = true;
        }
        // "webcam" mode: no real camera needed — inject a synthetic solid-colour frame source via the test
        // seam and verify the GPU compositing lands it at the expected picture-in-picture rectangle.
        if (mode == "webcam")
        {
            var s = host.Services.GetRequiredService<ISettingsService>();
            s.Current.WebcamPosition = WebcamOverlayPosition.BottomRight;
            s.Current.WebcamSizePercent = 20;
            var coord = host.Services.GetRequiredService<RecordingCoordinator>();
            coord.TestForceWebcamSource(new SolidColorWebcamFrameSource(320, 180, 0xFF, 0x00, 0xFF)); // magenta BGRA
        }
        // "split" mode: force the smallest allowed auto-split threshold and a high-bitrate quality so a
        // rollover happens quickly, to verify the segment rotation end-to-end.
        if (mode == "split")
        {
            var s = host.Services.GetRequiredService<ISettingsService>();
            s.Current.AutoSplitEnabled = true;
            s.Current.AutoSplitSizeMb = 100;
        }
        var coordinator = host.Services.GetRequiredService<RecordingCoordinator>();
        var probe = host.Services.GetRequiredService<RecMode.Encoding.Encoders.IEncoderProbe>();
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");

        coordinator.Finished += finished =>
        {
            string extra = "";
            if (mode is "split" or "downgrade")
            {
                string dir = System.IO.Path.GetDirectoryName(finished.OutputPath) ?? paths.RecordingsDirectory;
                string stem = System.IO.Path.GetFileNameWithoutExtension(finished.OutputPath).Split("_part")[0];
                int segments = System.IO.Directory.GetFiles(dir, $"{stem}*.mp4").Length;
                extra = $"segments={segments}\n";
            }
            System.IO.File.WriteAllText(resultPath,
                $"success={finished.Success}\nexit={finished.ExitCode}\nframes={finished.FramesWritten}\npath={finished.OutputPath}\n{extra}");
            Log.Information("Self-test finished: {@Result}", finished);
            dispatcher.BeginInvoke(() => shutdown(finished.Success ? 0 : 3));
        };

        Task.Run(() =>
        {
            try
            {
                var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
                var monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                var encoders = probe.GetAvailableEncoders();
                var encoder = encoders.FirstOrDefault(x => x is { Codec: VideoCodec.H264, IsHardware: true })
                    ?? encoders.First(x => x.Codec == VideoCodec.H264);

                var target = region
                    ? RecMode.Capture.CaptureTarget.FromRegion(monitor, new RecMode.Capture.RegionRect(100, 100, 1280, 720))
                    : RecMode.Capture.CaptureTarget.FromMonitor(monitor);
                int quality = mode == "split" ? 100 : 70;
                if (!coordinator.Start(target, encoder, MediaContainer.Mp4, 60, quality))
                {
                    System.IO.File.WriteAllText(resultPath, "success=false\nreason=start-returned-false\n");
                    dispatcher.BeginInvoke(() => shutdown(3));
                    return;
                }

                if (mode == "pause")
                {
                    // 3s record, 2s paused (should contribute no frames), 3s record → ~6s / ~360 frames.
                    Thread.Sleep(3000);
                    coordinator.Pause();
                    Thread.Sleep(2000);
                    coordinator.Resume();
                    Thread.Sleep(3000);
                }
                else if (mode == "split")
                {
                    Thread.Sleep(280000); // static-desktop content compresses hard; needs real time to cross the 100 MB floor
                }
                else if (mode == "downgrade")
                {
                    // Force the mid-stream hw→sw fallback deterministically (a real overload can't be reliably
                    // reproduced on this hardware) — same rotation path the health check would trigger.
                    Thread.Sleep(2000);
                    coordinator.TestForceDowngrade();
                    Thread.Sleep(4000);
                }
                else
                {
                    Thread.Sleep(6000);
                }

                coordinator.Stop();
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
                dispatcher.BeginInvoke(() => shutdown(3));
            }
        });
    }

    private async Task RunOverlaySelfTestAsync()
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var os = host.Services.GetRequiredService<IOsCapabilities>();
            var record = host.Services.GetRequiredService<RecordViewModel>();
            record.EnsureDevicesLoaded();
            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            var target = RecMode.Capture.CaptureTarget.FromMonitor(mon);

            string Save(string name)
            {
                var img = RecMode.Capture.ScreenshotCapturer.Capture(target)!;
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(img.Width, img.Height, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, img.Bgra, img.Stride);
                bmp.Freeze();
                string path = System.IO.Path.Combine(paths.DataDirectory, name);
                using var fs = System.IO.File.Create(path);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                enc.Save(fs);
                return path;
            }

            // 1) Overlays WITH exclusion off → they should appear in the WGC capture.
            var countdown = new CountdownWindow(mon, 9, os, excludeFromCapture: false);
            countdown.Show();
            var barVisible = new RecordingToolbarWindow(record, os, excludeFromCapture: false);
            barVisible.Show();
            await Task.Delay(900);
            string visible = Save("overlays-visible.png");
            countdown.Close();
            barVisible.Close();

            // 2) Toolbar WITH exclusion on → it should be absent from the WGC capture.
            var barExcluded = new RecordingToolbarWindow(record, os, excludeFromCapture: true);
            barExcluded.Show();
            await Task.Delay(900);
            string excluded = Save("overlays-excluded.png");
            barExcluded.Close();

            System.IO.File.WriteAllText(resultPath,
                $"success=true\nsupportsExclude={os.SupportsExcludeFromCapture}\nvisible={visible}\nexcluded={excluded}\n");
            shutdown(0);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            shutdown(3);
        }
    }

    private async Task RunRippleSelfTestAsync()
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

            var overlay = new ClickRippleOverlay();
            overlay.Show();
            await Task.Delay(200);
            // ripple at the monitor centre (screen/physical coords)
            overlay.AddRipple(mon.X + mon.Width / 2, mon.Y + mon.Height / 2);
            await Task.Delay(200); // catch it mid-animation

            var img = RecMode.Capture.ScreenshotCapturer.Capture(RecMode.Capture.CaptureTarget.FromMonitor(mon))!;
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(img.Width, img.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, img.Bgra, img.Stride);
            bmp.Freeze();
            string path = System.IO.Path.Combine(paths.DataDirectory, "ripple.png");
            using (var fs = System.IO.File.Create(path))
            {
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                enc.Save(fs);
            }

            overlay.Close();
            System.IO.File.WriteAllText(resultPath, $"success=true\nripple={path}\ncenter={mon.Width / 2},{mon.Height / 2}\n");
            shutdown(0);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            shutdown(3);
        }
    }

    private async Task RunAnnotateSelfTestAsync()
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

            var overlay = new AnnotationOverlay(() => { });
            overlay.Show();
            await Task.Delay(200);

            // Draw a diagonal stroke across the monitor centre (DIP coords).
            var pts = new System.Windows.Input.StylusPointCollection();
            for (int i = 0; i <= 20; i++)
            {
                pts.Add(new System.Windows.Input.StylusPoint(600 + i * 30, 400 + i * 15));
            }
            overlay.Canvas.Strokes.Add(new System.Windows.Ink.Stroke(pts, overlay.Canvas.DefaultDrawingAttributes));
            await Task.Delay(150);

            var img = RecMode.Capture.ScreenshotCapturer.Capture(RecMode.Capture.CaptureTarget.FromMonitor(mon))!;
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(img.Width, img.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, img.Bgra, img.Stride);
            bmp.Freeze();
            string path = System.IO.Path.Combine(paths.DataDirectory, "annotate.png");
            using (var fs = System.IO.File.Create(path))
            {
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                enc.Save(fs);
            }

            overlay.Close();
            System.IO.File.WriteAllText(resultPath, $"success=true\nannotate={path}\nstrokes=1\n");
            shutdown(0);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            shutdown(3);
        }
    }

    /// <summary>Test-only fake for <c>--selftest-webcam</c>: a fixed solid-colour BGRA frame, so the GPU
    /// picture-in-picture compositing can be verified without real camera hardware.</summary>
    private sealed class SolidColorWebcamFrameSource : RecMode.Capture.Webcam.IWebcamFrameSource
    {
        private readonly byte[] _frame;
        private readonly int _width, _height;

        public SolidColorWebcamFrameSource(int width, int height, byte b, byte g, byte r)
        {
            _width = width;
            _height = height;
            _frame = new byte[width * height * 4];
            for (int i = 0; i < _frame.Length; i += 4)
            {
                _frame[i] = b;
                _frame[i + 1] = g;
                _frame[i + 2] = r;
                _frame[i + 3] = 0xFF;
            }
        }

        public bool TryGetLatestFrame(out byte[] data, out int width, out int height, out int stride)
        {
            data = _frame;
            width = _width;
            height = _height;
            stride = _width * 4;
            return true;
        }
    }
}
