using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RecMode.App.Views;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using Serilog;

namespace RecMode.App;

public partial class App : Application
{
    private IHost? _host;
    private ICrashReporter? _crash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        if (Array.Exists(e.Args, a => a == "--selftest-record"))
        {
            RunSelfTest(paths);
            return;
        }

        var shell = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow = shell;
        shell.Show();
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

    private void RunSelfTest(IAppPaths paths)
    {
        var coordinator = _host!.Services.GetRequiredService<Services.RecordingCoordinator>();
        var probe = _host.Services.GetRequiredService<RecMode.Encoding.Encoders.IEncoderProbe>();
        string resultPath = System.IO.Path.Combine(paths.DataDirectory, "selftest-result.txt");

        coordinator.Finished += result =>
        {
            System.IO.File.WriteAllText(resultPath,
                $"success={result.Success}\nexit={result.ExitCode}\nframes={result.FramesWritten}\npath={result.OutputPath}\n");
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

                var target = RecMode.Capture.CaptureTarget.FromMonitor(monitor);
                if (!coordinator.Start(target, encoder, RecMode.Core.Settings.MediaContainer.Mp4, 60, 70))
                {
                    System.IO.File.WriteAllText(resultPath, "success=false\nreason=start-returned-false\n");
                    Dispatcher.BeginInvoke(() => Shutdown(3));
                    return;
                }

                System.Threading.Thread.Sleep(6000);
                coordinator.Stop();
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(resultPath, $"success=false\nexception={ex}\n");
                Dispatcher.BeginInvoke(() => Shutdown(3));
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _crash?.MarkSessionEndedCleanly();
        Log.Information("RecMode exiting cleanly");
        Log.CloseAndFlush();

        if (_host is not null)
        {
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
