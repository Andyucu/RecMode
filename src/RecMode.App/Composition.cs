using Microsoft.Extensions.DependencyInjection;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using RecMode.Encoding.Ffmpeg;
using RecMode.Interop.Diagnostics;

namespace RecMode.App;

/// <summary>DI composition root. Services are singletons; view models and windows are transient.</summary>
internal static class Composition
{
    public static IServiceCollection AddRecMode(this IServiceCollection services)
    {
        // Foundation services (RecMode.Core).
        services.AddSingleton<IAppPaths>(_ => new AppPaths());
        services.AddSingleton<IOsCapabilities>(_ => new OsCapabilities());
        services.AddSingleton<IErrorReporter, ErrorReporter>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Crash safety: minidump writer (Interop) + reporter gated on the opt-in setting (§3.6).
        services.AddSingleton<IMinidumpWriter, MinidumpWriter>();
        services.AddSingleton<ICrashReporter>(sp => new CrashReporter(
            sp.GetRequiredService<IAppPaths>(),
            sp.GetRequiredService<IMinidumpWriter>(),
            () => sp.GetRequiredService<ISettingsService>().Current.EnableCrashMinidumps));

        // Encoding foundation.
        services.AddSingleton<IFfmpegLocator, FfmpegLocator>();

        // UI.
        services.AddTransient<ShellViewModel>();
        services.AddTransient<ShellWindow>();

        return services;
    }
}
