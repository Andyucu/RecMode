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
            new SelfTest.SelfTestRunner(_host, paths, Dispatcher, code => Shutdown(code)).Run(selfTest["--selftest-".Length..]);
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
}
