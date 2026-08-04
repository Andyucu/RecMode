using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RecMode.App.Views;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using Serilog;

namespace RecMode.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001:Types that own disposable fields should be disposable",
    Justification = "A WPF Application manages its lifetime via OnExit, where the host and single-instance guard are disposed.")]
public partial class App : Application
{
    private IHost? _host;
    private ICrashReporter? _crash;
    private Services.SingleInstance? _singleInstance;
    private Services.ShellPresenter? _presenter;

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
        // guard is skipped for --selftest-* so headless checks can always spin up their own process — but
        // only in a build that actually compiles the self-test harness in (RECMODE_SELFTEST, Debug builds
        // only — see the csproj). Without the #if, any --selftest-* on the command line would bypass the
        // single-instance guard in a *shipped* build too, letting a second full instance start (hooks,
        // hotkeys, its own RecordingCoordinator) even though the harness itself isn't even present to run.
        bool isSelfTest = false;
#if RECMODE_SELFTEST
        isSelfTest = Array.Exists(e.Args, a => a.StartsWith("--selftest-", StringComparison.Ordinal));
#endif
        if (!isSelfTest)
        {
            _singleInstance = new Services.SingleInstance();
            if (!_singleInstance.TryAcquireOwnership())
            {
                // The mutex is only ever released by the OS on process exit, so failing to acquire it means
                // some RecMode process is genuinely still alive — but its pipe might not be listening yet, or
                // ever (hung instance, or a same-user squatter the SID check correctly refused to trust). A
                // discarded result here used to make this instance vanish silently in exactly that case: the
                // user's command line (including a plain double-click) went nowhere and nothing launched.
                if (!Services.SingleInstance.TryForwardToPrimary(e.Args))
                {
                    MessageBox.Show(
                        "RecMode appears to already be running, but couldn't be reached to hand off this request.\n\n" +
                        "Check Task Manager for an existing RecMode process; if none is genuinely running, this may be a stale lock left behind by a crash.",
                        "RecMode", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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

        // §3.5/security: a portable install extracted directly off a drive root (rather than inside the
        // user's own profile) inherits Windows' default ACL for that location, which typically grants
        // Authenticated Users: Modify — verified empirically. That means every other local account can read
        // every recording/screenshot/settings file this folder holds, and write into it (including replacing
        // the app's own bundled DLLs, since this is a self-contained publish). Detected, not silently fixed —
        // rewriting an ACL on a folder this process doesn't necessarily own risks a half-applied change or
        // locking the current user out of their own data, worse than the exposure itself. One-time,
        // dismissable: this is advisory, not a hard failure, and the check only fires for portable installs
        // (an installed build normally lives under Program Files, which standard users can't write to).
        if (paths.IsPortable && RecMode.Core.Infrastructure.FolderAclCheck.GrantsWriteToBroadGroups(paths.AppDirectory))
        {
            MessageBox.Show(
                $"RecMode's folder is readable and writable by every account on this PC:\n{paths.AppDirectory}\n\n" +
                "This usually happens when a portable copy is extracted directly to a drive root (e.g. C:\\ or D:\\) " +
                "instead of inside your user folder. Your recordings, screenshots, and settings are exposed to other " +
                "accounts on this machine.\n\n" +
                "Move this folder into your user profile (e.g. Documents or Desktop) to keep it private.",
                "RecMode — shared location",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        ConfigureLogging(paths);

        // A bare HostBuilder rather than Host.CreateDefaultBuilder(): the default pulls in config providers
        // (appsettings.json/environment-variable probing), a console lifetime, and other ASP.NET-oriented
        // defaults this WPF app never uses (settings persistence is entirely ISettingsService's own JSON file,
        // not IConfiguration) — trimming it is a small, safe startup-speed win with no behavior change.
        _host = new HostBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                // Reuse the already-resolved paths instance so logging and DI agree.
                services.AddSingleton<IAppPaths>(paths);
                services.AddRecMode();
            })
            .Build();

        // Crash-safety wiring must come before settings load: a settings file bad enough to escape Load()'s
        // own recovery (e.g. malformed JSON that parses but throws on materialization) must not crash the
        // process before a handler exists to catch it.
        _crash = _host.Services.GetRequiredService<ICrashReporter>();
        RegisterGlobalExceptionHandlers();

        var settingsService = _host.Services.GetRequiredService<ISettingsService>();
        settingsService.Load();

        // Startup-speed: the encoder probe trial-encodes every catalog entry via real ffmpeg subprocesses (or,
        // once disk-cached, just deserializes a small JSON file) — kick it off now, on a background thread, so
        // it races window creation instead of blocking it. Must come after settingsService.Load() (the probe
        // reads the ffmpeg-path-override setting via IFfmpegLocator). RecordViewModel.LoadDevices() still calls
        // GetAvailableEncoders() synchronously once the Record screen becomes active — EncoderProbe's own lock
        // just makes that call wait out whatever's left of this background probe instead of starting cold.
        var encoderProbe = _host.Services.GetRequiredService<IEncoderProbe>();
        bool isFirstRun = settingsService.IsFirstRun; // snapshot before any background task can race Save()
        System.Threading.Tasks.Task.Run(() =>
        {
            System.Collections.Generic.IReadOnlyList<RecMode.Encoding.Encoders.EncoderInfo> available = encoderProbe.GetAvailableEncoders();

            // First-run only: benchmark actual encode throughput (not just "does it open," which the probe
            // above already answered) and recommend the fastest one with real-time headroom, preferring
            // hardware per §3.9. Bounded to H.264 candidates — the universal baseline codec, and small enough
            // (typically 2-4 encoders on real hardware) to stay a background-only cost that never delays the
            // Record screen. A returning user's own encoder choice is never overridden.
            if (isFirstRun)
            {
                var h264Candidates = available.Where(e => e.Codec == RecMode.Core.Settings.VideoCodec.H264).ToList();
                RecMode.Encoding.Ffmpeg.IFfmpegLocator ffmpegLocator = _host.Services.GetRequiredService<RecMode.Encoding.Ffmpeg.IFfmpegLocator>();
                RecMode.Encoding.Ffmpeg.FfmpegResolution ff = ffmpegLocator.Resolve();
                var coordinator = _host.Services.GetRequiredService<Services.RecordingCoordinator>();

                // Abort the moment the user starts recording: each benchmarked candidate opens a real
                // hardware encoder session, and consumer NVENC/AMF/QSV drivers cap concurrent sessions — so
                // a benchmark still running when a brand-new user hits Record would push their very first
                // recording onto a software fallback with a Degraded warning.
                if (ff.IsAvailable && ff.FfmpegPath is not null &&
                    RecMode.Encoding.Encoders.EncoderBenchmark.Recommend(
                        ff.FfmpegPath, h264Candidates, shouldAbort: () => coordinator.IsRecording) is { } recommended)
                {
                    settingsService.Current.Codec = recommended.Codec;
                    settingsService.Current.Backend = recommended.Backend;
                    settingsService.Save();

                    // Also apply it to the already-loaded Record screen — persisting alone only took effect
                    // on the next launch. See RecordViewModel.ApplyRecommendedEncoder, which declines if the
                    // user has since chosen an encoder or a recording has started.
                    Dispatcher.BeginInvoke(() =>
                        _host.Services.GetRequiredService<ViewModels.RecordViewModel>()
                             .ApplyRecommendedEncoder(recommended.Codec, recommended.Backend));
                }
            }
        });

        // First-run only: if a microphone is physically connected, default the Mic toggle on instead of
        // off, so a new user with a mic doesn't silently record video-only. Returning users' explicit
        // choice (persisted from here on) is never overridden.
        if (settingsService.IsFirstRun && RecMode.Audio.MicrophoneDevices.IsAnyConnected())
        {
            settingsService.Current.MicrophoneEnabled = true;
            settingsService.Save();
        }

        _crash.MarkSessionStarted();

        if (_crash.PreviousSessionCrashed)
        {
            Log.Warning("Previous session did not shut down cleanly; recovery flow will be offered in a later phase.");
        }

        Log.Information("RecMode starting — portable={Portable}, data={Data}", paths.IsPortable, paths.DataDirectory);

        // Apply theme/accent before the first window paints.
        var settings = settingsService;
        var theme = _host.Services.GetRequiredService<Themes.ThemeManager>();
        theme.Apply(settings.Current.Theme, settings.Current.Accent);

#if RECMODE_SELFTEST
        // Headless verification hook: drive the production RecordingCoordinator for a few seconds and exit,
        // writing the outcome to Data\selftest-result.txt. Debug-only (RECMODE_SELFTEST, see the csproj) —
        // this is a manual integration-test harness for GPU/WGC/ffmpeg/WASAPI behavior the pure-logic test
        // projects can't reach, not something meant to ship.
        string? selfTest = Array.Find(e.Args, a => a.StartsWith("--selftest-", StringComparison.Ordinal));
        if (selfTest is not null)
        {
            new SelfTest.SelfTestRunner(_host, paths, Dispatcher, code => Shutdown(code)).Run(selfTest["--selftest-".Length..]);
            return;
        }
#endif

        var options = Services.CommandLineOptions.Parse(e.Args);

        // ShellPresenter resolves either the full ShellWindow (Sidebar/TopTab) or the small CompactWindow
        // (Compact layout — plan §1 "compact launcher") based on the current setting, and swaps between them
        // live if the setting changes later.
        _presenter = _host.Services.GetRequiredService<Services.ShellPresenter>();
        MainWindow = _presenter.Current;

        // --tray starts headless: the window stays hidden and the app is kept alive by the tray HWND (no
        // shown window ever closes, so OnLastWindowClose doesn't fire). Everything else shows the window.
        if (!options.Tray)
        {
            _presenter.Show();
        }
        else
        {
            // A shown window's IsVisibleChanged is what normally triggers ShellViewModel.EnsureInitialPageLoaded
            // → RecordViewModel.LoadDevices() — which never fires here, since no window is ever shown. Without
            // this, Monitors/Encoders/SelectedEncoder all stay empty for the entire headless session: F9, the
            // tray "Start/stop recording" menu item, and tray Screenshot all silently no-op (RecordCommand.
            // CanExecute needs a resolved source + encoder), with no error and nothing logged. This is exactly
            // the launch mode "Start with Windows" uses (StartupManager registers "<exe>" --tray), so a user who
            // enables that setting and never opens the window at least once would find every tray/hotkey action
            // dead. EnsureDevicesLoaded() is idempotent, so this is harmless even if a later CLI action
            // (--tray --record) or opening the window also calls it.
            _host.Services.GetRequiredService<ViewModels.RecordViewModel>().EnsureDevicesLoaded();
        }

        // MVP UX: global hotkeys + tray icon (Phase 5). Resolved on the UI thread (hotkeys need a message pump).
        _host.Services.GetRequiredService<Services.HotkeyBindings>().Register();
        _host.Services.GetRequiredService<Services.TrayIconService>().Attach(_presenter);
        _host.Services.GetRequiredService<Services.RecordingToolbar>().Attach();
        _host.Services.GetRequiredService<Services.SchedulerService>().Start();
        _host.Services.GetRequiredService<Services.ClickHighlightService>().Attach();
        _host.Services.GetRequiredService<Services.KeystrokeVisualizerService>().Attach();
        _host.Services.GetRequiredService<Services.SmartZoomService>().Attach();
        _host.Services.GetRequiredService<Services.ManualZoomService>().Attach();
        _host.Services.GetRequiredService<Services.SourceContourService>().Attach();
        _host.Services.GetRequiredService<Services.AnnotationService>().Attach();
        _host.Services.GetRequiredService<Services.AutoPauseGuardService>().Attach();

        // Recover any recordings orphaned by a previous crash (safe-recording payoff), off the UI thread.
        var recovery = _host.Services.GetRequiredService<Services.OrphanRecoveryService>();
        System.Threading.Tasks.Task.Run(recovery.RecoverOrphans);

        // Self-heal a "start with Windows" Run-key entry left pointing at a portable install that's since
        // moved (see StartupManager.ReconcileAfterMove) - a no-op for everyone who never enabled it. Gated on
        // this install's own persisted opt-in, not just on there being a stale entry at all — see the method
        // doc for why.
        _host.Services.GetRequiredService<Services.IStartupManager>().ReconcileAfterMove(settings.Current.StartWithWindows);

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
            _ = record.StartRecordingFromCli(); // automation starts immediately (no pre-roll countdown)
        }

        if (options.Stop)
        {
            // Not gated on coordinator.IsRecording: RecordViewModel.RequestStop() already no-ops when
            // there's genuinely nothing to stop, and — unlike that guard — also handles the case where a
            // start is still in flight (pre-flight/webcam/encoder startup can take several seconds), so
            // `RecMode --record` immediately followed by `RecMode --stop` reliably stops the recording
            // instead of silently dropping the stop because IsRecording hadn't flipped true yet.
            record.RequestStop();
        }

        // A second launch (or a plain re-launch with no action) surfaces the existing window, unless it
        // explicitly asked to stay in the tray.
        if (!startup && !options.Tray)
        {
            ShowMainWindow();
        }
    }

    private void ShowMainWindow() => _presenter?.Show();

    private static void ConfigureLogging(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Async(a => a.File(
                Path.Combine(paths.LogsDirectory, "recmode-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                // Without an explicit size limit, Serilog's default 1 GB-per-file cap still applies, but with
                // no rollOnFileSizeLimit a day that hits it just silently STOPS logging for the rest of that
                // day instead of rolling to a new file — and 7 retained files with no per-file cap at all is
                // a theoretical 7 GB on-disk bound inside the portable app folder, which sits oddly next to
                // this app's portable-first "nothing sprawls" story. 32 MB/file × 7 retained ≈ 224 MB worst
                // case, and logging never silently goes dark mid-day again.
                fileSizeLimitBytes: 32 * 1024 * 1024,
                rollOnFileSizeLimit: true,
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

    protected override void OnExit(ExitEventArgs e)
    {
        // Stop through the coordinator before DI disposes it. Disposing a live ffmpeg session kills the
        // process and leaves a partial recording; Stop() closes stdin, waits for the muxer and finalizes it.
        if (_host is not null)
        {
            try
            {
                var coordinator = _host.Services.GetRequiredService<Services.RecordingCoordinator>();
                if (coordinator.IsRecording)
                {
                    coordinator.Stop();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to finalize the active recording during shutdown");
            }
        }

        _crash?.MarkSessionEndedCleanly();
        Log.Information("RecMode exiting cleanly");

        _singleInstance?.Dispose();

        // Host disposal tears down every DI-registered service (trackers, watchers, hooks, etc.), several of
        // which log from their own Dispose() — closing the logger before this ran silently swallowed exactly
        // the diagnostics a shutdown-time bug would need. Log.CloseAndFlush() must be the last thing this
        // method does.
        if (_host is not null)
        {
            _host.Dispose();
        }

        Log.CloseAndFlush();

        base.OnExit(e);
    }
}
