using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RecMode.App.Services;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using RecMode.Encoding.Ffmpeg;
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

        // A/V sync soak: a long recording with periodic synchronized flash+beep markers, to verify sync
        // holds (no drift, no fixed offset) over a duration far longer than any other self-test exercises.
        if (mode == "avsync")
        {
            _ = RunAvSyncSoakSelfTestAsync();
            return;
        }

        // Annotation verification: draw a stroke on the overlay and WGC-capture it (not excluded).
        if (mode == "annotate")
        {
            _ = RunAnnotateSelfTestAsync();
            return;
        }

        // Annotation-on-Window verification: WGC's per-window capture can't see a separate overlay window
        // drawn on top of it, so draw-on-screen annotation used to be silently invisible for Window-source
        // recordings. Proves RecordingCoordinator.SetAnnotating's Region-proxy substitution actually gets the
        // ink into the encoded file for a real Window target, not just a monitor.
        if (mode == "annotate-window")
        {
            _ = RunAnnotateWindowSelfTestAsync();
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
        // "brightness" mode: crank the brightness slider to its max so a downstream ffmpeg signalstats
        // check can prove the GPU VideoProcessor filter actually changed the recorded pixels.
        if (mode == "brightness")
        {
            var s = host.Services.GetRequiredService<ISettingsService>();
            s.Current.Brightness = 100;
        }
        // "split" mode: force the smallest allowed auto-split threshold and a high-bitrate quality so a
        // rollover happens quickly, to verify the segment rotation end-to-end.
        if (mode == "split")
        {
            var s = host.Services.GetRequiredService<ISettingsService>();
            s.Current.AutoSplitEnabled = true;
            s.Current.AutoSplitSizeMb = 100;
        }
        // "keystroke" mode: turns on the keystroke visualizer, then injects a real Ctrl+Z key press via
        // SendInput mid-recording — exercising the actual GlobalKeyboardHook + KeystrokeVisualizerService wiring
        // end to end (not just the overlay window in isolation), so the recorded frames can be inspected to
        // confirm the "Ctrl + Z" pill genuinely renders into the encoded video, not just a live desktop screenshot.
        if (mode == "keystroke")
        {
            var s = host.Services.GetRequiredService<ISettingsService>();
            s.Current.ShowKeystrokes = true;
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

                var target = mode == "alldisplays" ? RecMode.Capture.CaptureTarget.FromAllDisplays(monitors)
                    : region ? RecMode.Capture.CaptureTarget.FromRegion(monitor, new RecMode.Capture.RegionRect(100, 100, 1280, 720))
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
                else if (mode == "keystroke")
                {
                    Thread.Sleep(1500);
                    SendCtrlZ();
                    Thread.Sleep(2000); // keep recording while the pill pops in/holds/fades (~1.35s cycle)
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_Z = 0x5A;

    /// <summary>Injects a real Ctrl+Z key press via <c>keybd_event</c> — indistinguishable from hardware input
    /// to <see cref="Services.GlobalKeyboardHook"/>'s WH_KEYBOARD_LL hook, so this exercises the actual
    /// production input path rather than calling into the overlay/service directly.</summary>
    private static void SendCtrlZ()
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_Z, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        keybd_event(VK_Z, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private async Task RunOverlaySelfTestAsync()
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var os = host.Services.GetRequiredService<IOsCapabilities>();
            var settings = host.Services.GetRequiredService<RecMode.Core.Settings.ISettingsService>();
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
            var barVisible = new RecordingToolbarWindow(record, os, settings, excludeFromCapture: false);
            barVisible.Show();
            await Task.Delay(900);
            string visible = Save("overlays-visible.png");
            countdown.Close();
            barVisible.Close();

            // 2) Toolbar WITH exclusion on → it should be absent from the WGC capture.
            var barExcluded = new RecordingToolbarWindow(record, os, settings, excludeFromCapture: true);
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

            var overlay = new ClickRippleOverlay(target: null); // falls back to the primary monitor
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

            var overlay = new AnnotationOverlay(() => { }, null);
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

    /// <summary>
    /// <c>--selftest-annotate-window</c>: records a real Window-source target (a freshly launched Notepad),
    /// toggles <see cref="RecordingCoordinator.SetAnnotating"/> mid-recording exactly like the toolbar's Draw
    /// button does, draws a bright red stroke via the real <see cref="AnnotationOverlay"/>, then extracts a
    /// frame from the encoded output and checks for red pixels — proving the ink actually reached the video,
    /// which Window-source capture couldn't do before the Region-proxy substitution.
    /// </summary>
    private async Task RunAnnotateWindowSelfTestAsync()
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        System.Diagnostics.Process? notepad = null;
        RecMode.Capture.WindowInfo? win = null;
        try
        {
            notepad = System.Diagnostics.Process.Start("notepad.exe");
            for (int i = 0; i < 50 && win is null; i++)
            {
                await Task.Delay(100);
                // Win11's Notepad is a packaged app: the process Process.Start returns isn't the one that
                // owns the real top-level window (that's hosted by ApplicationFrameHost or similar), so
                // MainWindowHandle never resolves — match by title across all capturable windows instead.
                win = RecMode.Capture.CaptureCapabilities.EnumerateWindows()
                    .FirstOrDefault(w => w.Title.Contains("Notepad", StringComparison.OrdinalIgnoreCase));
            }

            if (win is null)
            {
                System.IO.File.WriteAllText(resultPath, "success=false\nreason=notepad-window-not-found\n");
                shutdown(3);
                return;
            }

            var target = RecMode.Capture.CaptureTarget.FromWindow(win);
            var coordinator = host.Services.GetRequiredService<RecordingCoordinator>();
            var probe = host.Services.GetRequiredService<RecMode.Encoding.Encoders.IEncoderProbe>();
            var encoders = probe.GetAvailableEncoders();
            var encoder = encoders.FirstOrDefault(x => x is { Codec: VideoCodec.H264, IsHardware: true })
                ?? encoders.First(x => x.Codec == VideoCodec.H264);

            var finishedTcs = new TaskCompletionSource<RecordingResult>();
            coordinator.Finished += r => finishedTcs.TrySetResult(r);

            if (!coordinator.Start(target, encoder, MediaContainer.Mp4, 60, 90))
            {
                System.IO.File.WriteAllText(resultPath, "success=false\nreason=start-returned-false\n");
                shutdown(3);
                return;
            }

            await Task.Delay(1000); // let window-capture frames start flowing before annotating

            coordinator.SetAnnotating(true); // pacer applies the Region-proxy retarget on its next iteration
            await Task.Delay(300);

            var overlay = new AnnotationOverlay(() => { }, target);
            overlay.Canvas.DefaultDrawingAttributes = new System.Windows.Ink.DrawingAttributes
            {
                Color = System.Windows.Media.Colors.Red,
                Width = 40,
                Height = 40,
                FitToCurve = true,
            };
            overlay.Show();
            await Task.Delay(150);

            RecMode.Capture.CaptureCapabilities.TryGetWindowScreenRect(win.Handle, out RecMode.Capture.RegionRect winRect);
            int midY = winRect.Height / 2;
            var pts = new System.Windows.Input.StylusPointCollection();
            for (int i = 0; i <= 20; i++)
            {
                pts.Add(new System.Windows.Input.StylusPoint(20 + i * (winRect.Width - 40) / 20.0, midY));
            }
            overlay.Canvas.Strokes.Add(new System.Windows.Ink.Stroke(pts, overlay.Canvas.DefaultDrawingAttributes));

            await Task.Delay(2000); // several encoded frames with the stroke visible

            overlay.Close();
            coordinator.SetAnnotating(false); // revert to true window capture; must not crash even if this fails
            await Task.Delay(500);
            coordinator.Stop();

            RecordingResult result = await finishedTcs.Task;
            if (!result.Success)
            {
                System.IO.File.WriteAllText(resultPath, $"success=false\nreason=recording-failed\nexit={result.ExitCode}\n");
                shutdown(3);
                return;
            }

            string framePng = System.IO.Path.Combine(paths.DataDirectory, "annotate-window-frame.png");
            var ffmpeg = host.Services.GetRequiredService<IFfmpegLocator>();
            string ffmpegPath = ffmpeg.Resolve().FfmpegPath!;
            var psi = new System.Diagnostics.ProcessStartInfo(ffmpegPath,
                $"-y -ss 1.8 -i \"{result.OutputPath}\" -frames:v 1 -vf scale=64:64 \"{framePng}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using (var extract = System.Diagnostics.Process.Start(psi)!)
            {
                extract.WaitForExit(10000);
            }

            var bmp = new System.Windows.Media.Imaging.BitmapImage(new Uri(framePng));
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] buf = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(buf, stride, 0);
            int redPixels = 0;
            for (int i = 0; i + 2 < buf.Length; i += 4)
            {
                byte b = buf[i], g = buf[i + 1], r = buf[i + 2];
                if (r > 150 && g < 100 && b < 100)
                {
                    redPixels++;
                }
            }

            bool foundRed = redPixels > 5;
            System.IO.File.WriteAllText(resultPath,
                $"success={foundRed}\nredPixels={redPixels}\nframe={framePng}\npath={result.OutputPath}\n");
            shutdown(foundRed ? 0 : 3);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            shutdown(3);
        }
        finally
        {
            // Win11's Notepad is a packaged app: the real window usually belongs to a different process than
            // the one Process.Start returned, so both need to be cleaned up.
            try
            {
                if (win is { ProcessId: > 0 } w)
                {
                    var owner = System.Diagnostics.Process.GetProcessById(w.ProcessId);
                    owner.CloseMainWindow();
                    owner.WaitForExit(2000);
                    if (!owner.HasExited)
                    {
                        owner.Kill();
                    }
                }
            }
            catch (Exception) { }
            try { notepad?.CloseMainWindow(); notepad?.WaitForExit(2000); } catch (Exception) { }
            try { if (notepad is { HasExited: false }) { notepad.Kill(); } } catch (Exception) { }
        }
    }

    /// <summary>
    /// <c>--selftest-avsync</c>: a recording with a paired visual flash + audio beep fired at a regular
    /// interval, to measure real A/V offset (plan §1's "±40ms soak sync test") and confirm CFR video frame
    /// pacing holds (no drift, no stalls) over a duration much longer than any other self-test exercises.
    /// <para>
    /// The audio half was dropped for a full session in 2026-07-08 after investigation found full-system
    /// (<c>SystemAudioEnabled</c>) recordings producing a valid, correctly-timed AAC stream that was
    /// nevertheless completely silent. Re-verified 2026-07-28 with the exact same tone+<c>ffmpeg astats</c>
    /// methodology that session used: on this machine, right now, full-system audio genuinely carries real
    /// signal (confirmed via a fresh <c>--selftest-av</c> run with a played test tone, peak -18 dB / RMS
    /// -21 dB — not the bug's <c>-inf</c>). The 2026-07-24 <c>MixSource.ResolveFormat</c> fix (§CLAUDE.md) is
    /// the most likely explanation, though this dev machine's own WASAPI mix format was never able to
    /// reproduce the original silent-stream symptom either way, so that's inference, not direct proof of
    /// root-cause closure. Given audio demonstrably works today, the beep is restored here.
    /// </para>
    /// <para>
    /// Duration/interval are overridable via <c>RECMODE_SOAK_SECONDS</c>/<c>RECMODE_SOAK_INTERVAL_SECONDS</c>
    /// environment variables (defaults: 600s / 60s, matching the plan's own "2h soak" spirit scaled to
    /// something a single self-test invocation can actually run) — set lower for a quick smoke run.
    /// </para>
    /// </summary>
    private async Task RunAvSyncSoakSelfTestAsync()
    {
        int soakSeconds = GetEnvInt("RECMODE_SOAK_SECONDS", 600);
        int markerIntervalSeconds = GetEnvInt("RECMODE_SOAK_INTERVAL_SECONDS", 60);
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var settings = host.Services.GetRequiredService<ISettingsService>();
            settings.Current.SystemAudioEnabled = true;

            var coordinator = host.Services.GetRequiredService<RecordingCoordinator>();
            var probe = host.Services.GetRequiredService<RecMode.Encoding.Encoders.IEncoderProbe>();

            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            var encoders = probe.GetAvailableEncoders();
            var encoder = encoders.FirstOrDefault(x => x is { Codec: VideoCodec.H264, IsHardware: true })
                ?? encoders.First(x => x.Codec == VideoCodec.H264);
            var target = RecMode.Capture.CaptureTarget.FromMonitor(mon);

            RecordingResult? finished = null;
            coordinator.Finished += r => finished = r;

            // Frame rate is overridable so the A/V-offset measurement can be repeated at different rates —
            // if the offset scales with the frame interval it's tick quantization in the pacer/capture
            // hand-off; if it's constant in milliseconds it's a fixed pipeline latency difference. That
            // distinction decides which fix is even applicable, so it has to be measurable.
            int fps = GetEnvInt("RECMODE_SOAK_FPS", 60);
            if (!coordinator.Start(target, encoder, MediaContainer.Mp4, fps, 70))
            {
                System.IO.File.WriteAllText(resultPath, "success=false\nreason=start-returned-false\n");
                shutdown(3);
                return;
            }

            // Both marker emitters are created ONCE, before the loop, and reused for every marker.
            // Creating them per-marker (the original approach) meant each marker paid a fresh WPF window
            // creation + first-render and a fresh WASAPI render-device open — both cold-start costs in the
            // tens of milliseconds, and both highly variable. That put jitter of the same magnitude as the
            // offset being measured directly into the instrument: three runs of the per-marker version
            // produced per-marker offsets ranging from +22ms to -130ms, with two runs of the *identical*
            // configuration averaging -101ms and -50ms. A measurement whose noise is as large as its signal
            // can't support any conclusion about the pipeline, let alone a compensation constant.
            using var emitter = new SoakMarkerEmitter(mon);
            await Task.Delay(500); // let the pre-created window and audio device settle before the first use

            var markerTimesSec = new List<double>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double nextMarkerAt = 5; // small warm-up before the first marker (encoder-init ramp)
            while (sw.Elapsed.TotalSeconds < soakSeconds)
            {
                if (sw.Elapsed.TotalSeconds >= nextMarkerAt)
                {
                    markerTimesSec.Add(sw.Elapsed.TotalSeconds);
                    emitter.Fire();
                    nextMarkerAt += markerIntervalSeconds;
                }
                await Task.Delay(200);
            }

            coordinator.Stop();
            for (int i = 0; i < 100 && finished is null; i++)
            {
                await Task.Delay(100);
            }

            if (finished is not { Success: true })
            {
                System.IO.File.WriteAllText(resultPath, "success=false\nreason=recording-failed\n");
                shutdown(3);
                return;
            }

            string markers = string.Join(",", markerTimesSec.Select(t => t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            System.IO.File.WriteAllText(resultPath,
                $"success=true\npath={finished.OutputPath}\nmarkers={markers}\nsoakSeconds={soakSeconds}\n");
            Log.Information("A/V sync soak finished: {@Result} markers={Markers}", finished, markers);
            shutdown(0);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            shutdown(3);
        }
    }

    /// <summary>
    /// Emits paired flash+beep markers for <c>--selftest-avsync</c>, keeping both emitters <em>warm</em> —
    /// the full-screen flash window and the WASAPI render device are created once and reused, so firing a
    /// marker costs only a visibility toggle and a buffer write.
    /// <para>
    /// This matters more than it looks. The original per-marker version created a fresh <see cref="Window"/>
    /// and a fresh <c>WaveOutEvent</c> every time, so every measurement included a one-off WPF
    /// window-creation + first-render and a one-off audio-device open — both variable, both tens of
    /// milliseconds, and both entirely outside the pipeline being measured. That noise floor was the same
    /// size as the signal: per-marker offsets ranged +22ms..-130ms and two runs of an identical
    /// configuration averaged -101ms and -50ms. Warm emitters remove that from the instrument so the
    /// remaining variation is attributable to the capture/encode pipeline rather than to the measuring
    /// apparatus.
    /// </para>
    /// <para>
    /// The beep deliberately goes out through the ordinary default render device rather than through
    /// <see cref="RecMode.Audio.AudioMixer"/>: it must exercise the same "real sound plays, full-system
    /// loopback captures it" path a user's system audio takes, not the app's own capture-side mixer.
    /// </para>
    /// </summary>
    private sealed class SoakMarkerEmitter : IDisposable
    {
        private const int BeepMs = 150;
        private const int SampleRate = 48000;
        private const int Channels = 2;

        private readonly Window _flash;
        private readonly BufferedWaveProvider? _beepBuffer;
        private readonly WaveOutEvent? _output;
        private readonly byte[] _beepPcm;

        public SoakMarkerEmitter(RecMode.Capture.MonitorInfo monitor)
        {
            _flash = new Window
            {
                WindowStyle = WindowStyle.None,
                Background = Brushes.White,
                Left = monitor.X,
                Top = monitor.Y,
                Width = monitor.Width,
                Height = monitor.Height,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Visibility = Visibility.Hidden,
            };
            // Shown once (hidden) so the HWND, the render pass, and the DWM surface all exist up front;
            // per-marker cost is then just the visibility toggle.
            _flash.Show();

            _beepPcm = RenderBeep();
            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
                _beepBuffer = new BufferedWaveProvider(format)
                {
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = true,
                };
                // The render device's own output buffering sits between AddSamples() and the moment the tone
                // reaches the mix point that loopback taps - so it lands in the recording *late* by roughly
                // this much, and shows up in the analyzer as a positive offset that has nothing to do with
                // the capture pipeline. Overridable via RECMODE_SOAK_BEEP_LATENCY_MS specifically so that
                // bias can be demonstrated (vary it; a true pipeline offset would not move with it) and then
                // subtracted, rather than being silently folded into the reported result.
                _output = new WaveOutEvent { DesiredLatency = GetEnvInt("RECMODE_SOAK_BEEP_LATENCY_MS", 100) };
                _output.Init(_beepBuffer);
                _output.Play(); // stays playing (silent) for the whole soak; a marker just queues samples
            }
            catch (Exception ex)
            {
                // Best-effort: without audio the flash half alone still verifies frame pacing.
                Log.Warning(ex, "Soak marker audio device couldn't be opened; markers will be video-only");
                _output = null;
                _beepBuffer = null;
            }
        }

        /// <summary>One marker: show the flash and queue the beep as close together as this thread allows.</summary>
        public void Fire()
        {
            _beepBuffer?.AddSamples(_beepPcm, 0, _beepPcm.Length);
            _flash.Visibility = Visibility.Visible;
            _ = Task.Delay(BeepMs).ContinueWith(_ =>
                _flash.Dispatcher.BeginInvoke(() => _flash.Visibility = Visibility.Hidden));
        }

        /// <summary>Pre-renders the tone burst to raw 32-bit float PCM once, so firing a marker never pays
        /// generation cost. A short linear fade in/out avoids a click transient smearing the onset the
        /// analyzer is looking for.</summary>
        private static byte[] RenderBeep()
        {
            int frames = SampleRate * BeepMs / 1000;
            const int fade = 48; // ~1ms
            byte[] pcm = new byte[frames * Channels * sizeof(float)];
            for (int i = 0; i < frames; i++)
            {
                double envelope = Math.Min(1.0, Math.Min(i, frames - 1 - i) / (double)fade);
                float sample = (float)(0.8 * envelope * Math.Sin(2 * Math.PI * 1000 * i / SampleRate));
                for (int ch = 0; ch < Channels; ch++)
                {
                    BitConverter.TryWriteBytes(
                        pcm.AsSpan(((i * Channels) + ch) * sizeof(float), sizeof(float)), sample);
                }
            }
            return pcm;
        }

        public void Dispose()
        {
            try { _output?.Stop(); } catch (Exception ex) { Log.Debug(ex, "Soak marker output stop failed"); }
            _output?.Dispose();
            try { _flash.Close(); } catch (Exception ex) { Log.Debug(ex, "Soak marker flash close failed"); }
        }
    }

    /// <summary>Reads an integer override from the environment, falling back to <paramref name="default_"/>
    /// on anything missing or unparsable — used so <c>--selftest-avsync</c>'s duration/interval can be
    /// shortened for a quick smoke run without needing a dedicated CLI flag for a debug-only self-test.</summary>
    private static int GetEnvInt(string name, int default_) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) && value > 0 ? value : default_;


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

        public bool TryGetLatestFrame(ref byte[] destination, out int width, out int height, out int stride)
        {
            width = _width;
            height = _height;
            stride = _width * 4;
            if (destination.Length < _frame.Length)
            {
                destination = new byte[_frame.Length];
            }
            Buffer.BlockCopy(_frame, 0, destination, 0, _frame.Length);
            return true;
        }
    }
}
