using Microsoft.Extensions.DependencyInjection;
using RecMode.App.Services;
using RecMode.App.Themes;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Audio;
using RecMode.Capture;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Recording;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using RecMode.Encoding.Ffmpeg;
using RecMode.Interop.Diagnostics;

namespace RecMode.App;

/// <summary>DI composition root. Services are singletons; windows are resolved per show.</summary>
internal static class Composition
{
    public static IServiceCollection AddRecMode(this IServiceCollection services)
    {
        // Foundation (RecMode.Core).
        services.AddSingleton<IAppPaths>(_ => new AppPaths());
        services.AddSingleton<IOsCapabilities>(_ => new OsCapabilities());
        services.AddSingleton<IErrorReporter, ErrorReporter>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<RecordingStateMachine>();

        // Crash safety.
        services.AddSingleton<IMinidumpWriter, MinidumpWriter>();
        services.AddSingleton<ICrashReporter>(sp => new CrashReporter(
            sp.GetRequiredService<IAppPaths>(),
            sp.GetRequiredService<IMinidumpWriter>(),
            () => sp.GetRequiredService<ISettingsService>().Current.EnableCrashMinidumps));

        // Encoding + capture.
        services.AddSingleton<IFfmpegLocator, FfmpegLocator>();
        services.AddSingleton<IEncoderProbe, EncoderProbe>();
        services.AddSingleton<Func<ICaptureEngine>>(_ => () => new WgcCaptureEngine());
        services.AddSingleton<Func<IPreviewEngine>>(_ => () => new WgcPreviewEngine());
        services.AddSingleton<Func<IAudioMixer>>(_ => () => new AudioMixer());
        services.AddSingleton<RecordingCoordinator>();

        // Theming.
        services.AddSingleton<ThemeManager>();

        // Region selection.
        services.AddSingleton<IRegionPicker, RegionPicker>();

        // MVP UX services (Phase 5).
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<IScreenshotFlash, ScreenshotFlash>();
        services.AddSingleton<GlobalHotkeys>();
        services.AddSingleton<HotkeyBindings>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<ICountdownController, CountdownController>();
        services.AddSingleton<RecordingToolbar>();
        services.AddSingleton<IStartupManager, StartupManager>();
        services.AddSingleton<IScheduleEditor, ScheduleEditor>();
        services.AddSingleton<IProfileNamePrompt, ProfileNamePrompt>();
        services.AddSingleton<OrphanRecoveryService>();
        services.AddSingleton<IPowerStatus, PowerStatus>();
        services.AddSingleton<IDiskSpeedProbe, DiskSpeedProbe>();
        services.AddSingleton<RecMode.Core.Library.ILibraryIndex, RecMode.Core.Library.LibraryIndex>();
        services.AddSingleton<SchedulerService>();
        services.AddSingleton<GlobalMouseHook>();
        services.AddSingleton<ClickHighlightService>();
        services.AddSingleton<AnnotationService>();
        services.AddSingleton<IUpdateChecker, UpdateChecker>();

        // View models (singletons — one live page instance each, held by the shell).
        services.AddSingleton<RecordViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<ScheduleViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ShellViewModel>();

        // Windows.
        services.AddTransient<ShellWindow>();

        return services;
    }
}
