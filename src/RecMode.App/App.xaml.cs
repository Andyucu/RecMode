using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RecMode.App.Views;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using Serilog;

namespace RecMode.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable",
    Justification = "A WPF Application manages its lifetime via OnExit, where the host and single-instance guard are disposed.")]
public partial class App : Application
{
    private IHost? _host;
    private ICrashReporter? _crash;
    private Services.SingleInstance? _singleInstance;
    private ShellWindow? _shell;

    /// <summary>
    /// Hand-written entry point (App.xaml is a Page, not an ApplicationDefinition, so this replaces the
    /// usual WPF-generated <c>Main</c>) — needed so <c>VelopackApp.Build().Run()</c> can run before any WPF
    /// startup cost, per Velopack's recommended integration for apps without an easily-editable Main. Safe
    /// to call unconditionally: it's a no-op unless this process is a genuine Velopack-managed install
    /// (portable/dev runs fall straight through), which is how the same code serves both distribution modes
    /// (plan §3.5).
    /// </summary>
    [STAThread]
    public static void Main()
    {
        Velopack.VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The app owns its own lifetime: it can run headless from the tray, and transient overlay windows
        // (countdown, recording toolbar) must not end the process when they close. Everything that should
        // quit does so explicitly (main-window close button, tray Quit, self-tests).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Single-instance guard (before any expensive startup): a second launch forwards its command line to
        // the running instance and exits. Self-test runs bypass this so verification is never blocked. The
        // guard is skipped for --selftest-* so headless checks can always spin up their own process.
        bool isSelfTest = Array.Exists(e.Args, a => a.StartsWith("--selftest-", StringComparison.Ordinal));
        if (!isSelfTest)
        {
            _singleInstance = new Services.SingleInstance();
            if (!_singleInstance.TryAcquireOwnership())
            {
                Services.SingleInstance.TryForwardToPrimary(e.Args);
                Shutdown(0);
                return;
            }
        }

        // Resolve paths first so logging and crash artifacts land in the right place (portable-aware).
        var paths = new AppPaths();
        if (!paths.IsDataDirectoryWritable())
        {
            MessageBox.Show(
                $"RecMode can't write to its data folder:\n{paths.DataDirectory}\n\n" +
                "If you're running from a zip, extract it to a writable location first.",
                "RecMode — read-only location",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        paths.EnsureDirectories();
        ConfigureLogging(paths);

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                // Reuse the already-resolved paths instance so logging and DI agree.
                services.AddSingleton<IAppPaths>(paths);
                services.AddRecMode();
            })
            .Build();

        // Settings load (with migration + corrupt recovery), then crash-safety wiring.
        _host.Services.GetRequiredService<ISettingsService>().Load();

        _crash = _host.Services.GetRequiredService<ICrashReporter>();
        RegisterGlobalExceptionHandlers();
        _crash.MarkSessionStarted();

        if (_crash.PreviousSessionCrashed)
        {
            Log.Warning("Previous session did not shut down cleanly; recovery flow will be offered in a later phase.");
        }

        Log.Information("RecMode starting — portable={Portable}, data={Data}", paths.IsPortable, paths.DataDirectory);

        // Apply theme/accent before the first window paints.
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        var theme = _host.Services.GetRequiredService<Themes.ThemeManager>();
        theme.Apply(settings.Current.Theme, settings.Current.Accent);

        // Headless verification hook (temporary; the real CLI arrives in Phase 5): drive the production
        // RecordingCoordinator for a few seconds and exit, writing the outcome to Data\selftest-result.txt.
        string? selfTest = Array.Find(e.Args, a => a.StartsWith("--selftest-", StringComparison.Ordinal));
        if (selfTest is not null)
        {
            RunSelfTest(paths, selfTest["--selftest-".Length..]);
            return;
        }

        var options = Services.CommandLineOptions.Parse(e.Args);

        _shell = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow = _shell;

        // --tray starts headless: the window stays hidden and the app is kept alive by the tray HWND (no
        // shown window ever closes, so OnLastWindowClose doesn't fire). Everything else shows the window.
        if (!options.Tray)
        {
            _shell.Show();
        }

        // MVP UX: global hotkeys + tray icon (Phase 5). Resolved on the UI thread (hotkeys need a message pump).
        _host.Services.GetRequiredService<Services.HotkeyBindings>().Register();
        _host.Services.GetRequiredService<Services.TrayIconService>().Attach(_shell);
        _host.Services.GetRequiredService<Services.RecordingToolbar>().Attach();
        _host.Services.GetRequiredService<Services.SchedulerService>().Start();
        _host.Services.GetRequiredService<Services.ClickHighlightService>().Attach();
        _host.Services.GetRequiredService<Services.AnnotationService>().Attach();

        // Recover any recordings orphaned by a previous crash (safe-recording payoff), off the UI thread.
        var recovery = _host.Services.GetRequiredService<Services.OrphanRecoveryService>();
        System.Threading.Tasks.Task.Run(recovery.RecoverOrphans);

        // Launch-time update check (plan §3.5): notify only, never auto-apply without the user explicitly
        // clicking "Update & restart" in Settings. Silent for NotConfigured/UpToDate/Failed — only a real
        // available update is worth interrupting the user for.
        if (settings.Current.CheckForUpdatesOnLaunch)
        {
            var updateChecker = _host.Services.GetRequiredService<Services.IUpdateChecker>();
            var errors = _host.Services.GetRequiredService<RecMode.Core.Errors.IErrorReporter>();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                Services.UpdateCheckResult result = await updateChecker.CheckAsync();
                if (result.Status == Services.UpdateCheckStatus.UpdateAvailable)
                {
                    errors.Warn("app.update-available", $"RecMode {result.Version} is available.",
                        "Open Settings to update.");
                }
            });
        }

        // Run any startup automation action (e.g. --record / --screenshot), then listen for commands forwarded
        // by future launches (single-instance). Forwarded commands are marshalled to the UI thread.
        ExecuteCliCommand(options, startup: true);
        _singleInstance?.StartListening(args =>
            Dispatcher.BeginInvoke(() => ExecuteCliCommand(Services.CommandLineOptions.Parse(args), startup: false)));
    }

    /// <summary>Applies a parsed command line to the running app (startup action or a forwarded second launch).</summary>
    private void ExecuteCliCommand(Services.CommandLineOptions options, bool startup)
    {
        if (_host is null)
        {
            return;
        }

        var record = _host.Services.GetRequiredService<ViewModels.RecordViewModel>();
        var coordinator = _host.Services.GetRequiredService<Services.RecordingCoordinator>();

        // A CLI action may run before the Record view is ever shown (e.g. --tray --record), so make sure a
        // default source + encoder are selected first.
        if (options.HasAction)
        {
            record.EnsureDevicesLoaded();
        }

        if (options.Screenshot)
        {
            record.TakeScreenshot();
        }

        if (options.Record && !coordinator.IsRecording && record.RecordCommand.CanExecute(null))
        {
            record.StartRecordingFromCli(); // automation starts immediately (no pre-roll countdown)
        }

        if (options.Stop && coordinator.IsRecording)
        {
            record.RecordCommand.Execute(null); // toggles off
        }

        // A second launch (or a plain re-launch with no action) surfaces the existing window, unless it
        // explicitly asked to stay in the tray.
        if (!startup && !options.Tray)
        {
            ShowMainWindow();
        }
    }

    private void ShowMainWindow()
    {
        if (_shell is null)
        {
            return;
        }

        _shell.Show();
        _shell.WindowState = WindowState.Normal;
        _shell.Activate();
    }

    private static void ConfigureLogging(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Async(a => a.File(
                Path.Combine(paths.LogsDirectory, "recmode-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true))
            .CreateLogger();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI exception");
        _crash?.RecordUnhandledException(e.Exception, isTerminating: false);
        MessageBox.Show(
            "RecMode hit an unexpected error. Details were written to the log.",
            "RecMode",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true; // keep the app alive; recording recovery arrives with the state machine (Phase 3/5)
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unhandled domain exception (terminating={Terminating})", e.IsTerminating);
            _crash?.RecordUnhandledException(ex, e.IsTerminating);
        }

        Log.CloseAndFlush();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        _crash?.RecordUnhandledException(e.Exception, isTerminating: false);
        e.SetObserved();
    }

    private void RunSelfTest(IAppPaths paths, string mode)
    {
        bool region = mode == "region";
        string result = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");

        // Overlay verification: capture the countdown + toolbar via the WGC path with and without capture
        // exclusion, to prove both that they render and that exclusion keeps them out of the recording.
        if (mode == "overlays")
        {
            _ = RunOverlaySelfTestAsync(paths);
            return;
        }

        // Click-ripple verification: show a ripple and WGC-capture it (not excluded → part of the recording).
        if (mode == "ripple")
        {
            _ = RunRippleSelfTestAsync(paths);
            return;
        }

        // Annotation verification: draw a stroke on the overlay and WGC-capture it (not excluded).
        if (mode == "annotate")
        {
            _ = RunAnnotateSelfTestAsync(paths);
            return;
        }

        // Screenshot runs synchronously on this (UI) thread — the clipboard copy needs STA.
        if (mode == "screenshot")
        {
            var svc = _host!.Services.GetRequiredService<Services.ScreenshotService>();
            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            string? path = svc.Capture(RecMode.Capture.CaptureTarget.FromMonitor(mon));
            System.IO.File.WriteAllText(result, $"success={path is not null}\npath={path}\n");
            Dispatcher.BeginInvoke(() => Shutdown(path is null ? 3 : 0));
            return;
        }

        // "av" mode: force system audio on so the recording gets an audio track.
        if (mode == "av")
        {
            var s = _host!.Services.GetRequiredService<ISettingsService>();
            s.Current.SystemAudioEnabled = true;
        }
        // "webcam" mode: no real camera needed — inject a synthetic solid-colour frame source via the test
        // seam and verify the GPU compositing lands it at the expected picture-in-picture rectangle.
        if (mode == "webcam")
        {
            var s = _host!.Services.GetRequiredService<ISettingsService>();
            s.Current.WebcamPosition = RecMode.Core.Settings.WebcamOverlayPosition.BottomRight;
            s.Current.WebcamSizePercent = 20;
            var coord = _host.Services.GetRequiredService<Services.RecordingCoordinator>();
            coord.TestForceWebcamSource(new SolidColorWebcamFrameSource(320, 180, 0xFF, 0x00, 0xFF)); // magenta BGRA
        }
        // "split" mode: force the smallest allowed auto-split threshold and a high-bitrate quality so a
        // rollover happens quickly, to verify the segment rotation end-to-end.
        if (mode == "split")
        {
            var s = _host!.Services.GetRequiredService<ISettingsService>();
            s.Current.AutoSplitEnabled = true;
            s.Current.AutoSplitSizeMb = 100;
        }
        var coordinator = _host!.Services.GetRequiredService<Services.RecordingCoordinator>();
        var probe = _host.Services.GetRequiredService<RecMode.Encoding.Encoders.IEncoderProbe>();
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");

        coordinator.Finished += result =>
        {
            string extra = "";
            if (mode is "split" or "downgrade")
            {
                string dir = System.IO.Path.GetDirectoryName(result.OutputPath) ?? paths.RecordingsDirectory;
                string stem = System.IO.Path.GetFileNameWithoutExtension(result.OutputPath).Split("_part")[0];
                int segments = System.IO.Directory.GetFiles(dir, $"{stem}*.mp4").Length;
                extra = $"segments={segments}\n";
            }
            System.IO.File.WriteAllText(resultPath,
                $"success={result.Success}\nexit={result.ExitCode}\nframes={result.FramesWritten}\npath={result.OutputPath}\n{extra}");
            Log.Information("Self-test finished: {@Result}", result);
            Dispatcher.BeginInvoke(() => Shutdown(result.Success ? 0 : 3));
        };

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
                var monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                var encoders = probe.GetAvailableEncoders();
                var encoder = encoders.FirstOrDefault(x => x is { Codec: RecMode.Core.Settings.VideoCodec.H264, IsHardware: true })
                    ?? encoders.First(x => x.Codec == RecMode.Core.Settings.VideoCodec.H264);

                var target = region
                    ? RecMode.Capture.CaptureTarget.FromRegion(monitor, new RecMode.Capture.RegionRect(100, 100, 1280, 720))
                    : RecMode.Capture.CaptureTarget.FromMonitor(monitor);
                int quality = mode == "split" ? 100 : 70;
                if (!coordinator.Start(target, encoder, RecMode.Core.Settings.MediaContainer.Mp4, 60, quality))
                {
                    System.IO.File.WriteAllText(resultPath, "success=false\nreason=start-returned-false\n");
                    Dispatcher.BeginInvoke(() => Shutdown(3));
                    return;
                }

                if (mode == "pause")
                {
                    // 3s record, 2s paused (should contribute no frames), 3s record → ~6s / ~360 frames.
                    System.Threading.Thread.Sleep(3000);
                    coordinator.Pause();
                    System.Threading.Thread.Sleep(2000);
                    coordinator.Resume();
                    System.Threading.Thread.Sleep(3000);
                }
                else if (mode == "split")
                {
                    System.Threading.Thread.Sleep(280000); // static-desktop content compresses hard; needs real time to cross the 100 MB floor
                }
                else if (mode == "downgrade")
                {
                    // Force the mid-stream hw→sw fallback deterministically (a real overload can't be reliably
                    // reproduced on this hardware) — same rotation path the health check would trigger.
                    System.Threading.Thread.Sleep(2000);
                    coordinator.TestForceDowngrade();
                    System.Threading.Thread.Sleep(4000);
                }
                else
                {
                    System.Threading.Thread.Sleep(6000);
                }

                coordinator.Stop();
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
                Dispatcher.BeginInvoke(() => Shutdown(3));
            }
        });
    }

    private async System.Threading.Tasks.Task RunOverlaySelfTestAsync(IAppPaths paths)
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var os = _host!.Services.GetRequiredService<IOsCapabilities>();
            var record = _host.Services.GetRequiredService<ViewModels.RecordViewModel>();
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
            var countdown = new Views.CountdownWindow(mon, 9, os, excludeFromCapture: false);
            countdown.Show();
            var barVisible = new Views.RecordingToolbarWindow(record, os, excludeFromCapture: false);
            barVisible.Show();
            await System.Threading.Tasks.Task.Delay(900);
            string visible = Save("overlays-visible.png");
            countdown.Close();
            barVisible.Close();

            // 2) Toolbar WITH exclusion on → it should be absent from the WGC capture.
            var barExcluded = new Views.RecordingToolbarWindow(record, os, excludeFromCapture: true);
            barExcluded.Show();
            await System.Threading.Tasks.Task.Delay(900);
            string excluded = Save("overlays-excluded.png");
            barExcluded.Close();

            System.IO.File.WriteAllText(resultPath,
                $"success=true\nsupportsExclude={os.SupportsExcludeFromCapture}\nvisible={visible}\nexcluded={excluded}\n");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            Shutdown(3);
        }
    }

    private async System.Threading.Tasks.Task RunRippleSelfTestAsync(IAppPaths paths)
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

            var overlay = new Views.ClickRippleOverlay();
            overlay.Show();
            await System.Threading.Tasks.Task.Delay(200);
            // ripple at the monitor centre (screen/physical coords)
            overlay.AddRipple(mon.X + mon.Width / 2, mon.Y + mon.Height / 2);
            await System.Threading.Tasks.Task.Delay(200); // catch it mid-animation

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
            Shutdown(0);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            Shutdown(3);
        }
    }

    private async System.Threading.Tasks.Task RunAnnotateSelfTestAsync(IAppPaths paths)
    {
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");
        try
        {
            var monitors = RecMode.Capture.CaptureCapabilities.EnumerateMonitors();
            var mon = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

            var overlay = new Views.AnnotationOverlay(() => { });
            overlay.Show();
            await System.Threading.Tasks.Task.Delay(200);

            // Draw a diagonal stroke across the monitor centre (DIP coords).
            var pts = new System.Windows.Input.StylusPointCollection();
            for (int i = 0; i <= 20; i++)
            {
                pts.Add(new System.Windows.Input.StylusPoint(600 + i * 30, 400 + i * 15));
            }
            overlay.Canvas.Strokes.Add(new System.Windows.Ink.Stroke(pts, overlay.Canvas.DefaultDrawingAttributes));
            await System.Threading.Tasks.Task.Delay(150);

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
            Shutdown(0);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
            Shutdown(3);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _crash?.MarkSessionEndedCleanly();
        Log.Information("RecMode exiting cleanly");
        Log.CloseAndFlush();

        _singleInstance?.Dispose();

        if (_host is not null)
        {
            _host.Dispose();
        }

        base.OnExit(e);
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
