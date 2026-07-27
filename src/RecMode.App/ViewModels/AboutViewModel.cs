using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;

namespace RecMode.App.ViewModels;

/// <summary>
/// About screen (plan Tier-5 backlog): version/runtime info, the privacy statement the plan calls for ("no
/// telemetry, ever" — see CLAUDE.md), and links out to the project, license, and third-party notices.
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    private readonly IAppPaths _paths;
    private readonly RecMode.Core.Errors.IErrorReporter _errors;

    public AboutViewModel(IAppPaths paths, RecMode.Core.Errors.IErrorReporter errors)
    {
        _paths = paths;
        _errors = errors;
        OpenGitHubCommand = new RelayCommand(() => OpenUrl(Services.UpdateChecker.GitHubRepositoryUrl));
        OpenLicenseCommand = new RelayCommand(OpenLicense);
        OpenThirdPartyNoticesCommand = new RelayCommand(OpenThirdPartyNotices);
    }

    public string VersionInfo
    {
        get
        {
            string? version = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return $"RecMode {version ?? "0.9.0-beta"}";
        }
    }

    public string RuntimeInfo => ".NET 10 · WPF · win-x64";

    public ICommand OpenGitHubCommand { get; }
    public ICommand OpenLicenseCommand { get; }
    public ICommand OpenThirdPartyNoticesCommand { get; }

    private void OpenLicense()
    {
        string path = Path.Combine(_paths.AppDirectory, "LICENSE");
        if (File.Exists(path))
        {
            OpenUrl(path);
        }
        else
        {
            // Absent in any `dotnet build` dev run, where LICENSE isn't copied next to the exe. Silently
            // doing nothing made this look like a dead button.
            _errors.Warn("about.license-missing", "The LICENSE file couldn't be found.",
                "It ships alongside RecMode.exe in a released build.");
        }
    }

    private void OpenThirdPartyNotices()
    {
        if (Directory.Exists(_paths.LicensesDirectory))
        {
            OpenUrl(_paths.LicensesDirectory);
        }
        else
        {
            _errors.Warn("about.notices-missing", "The third-party notices folder couldn't be found.",
                "It ships alongside RecMode.exe in a released build.");
        }
    }

    /// <summary>Shell-executes a URL or local path. Guarded because <c>UseShellExecute</c> throws when there's
    /// no handler for the target — no default browser, or no http association on a locked-down corporate
    /// image, both realistic for a portable app. Unhandled, that reached the global exception handler and
    /// showed the generic "RecMode hit an unexpected error" crash modal (plus a Log.Fatal) for a link click.</summary>
    private void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _errors.Warn("about.open-failed", "Couldn't open that link.",
                "No application is associated with it on this machine.", ex);
        }
    }
}
