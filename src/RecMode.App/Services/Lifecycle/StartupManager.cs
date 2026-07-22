using Microsoft.Win32;

namespace RecMode.App.Services;

/// <summary>Reads/writes the "start with Windows" state. A labelled §3.5 opt-in exception to portable containment.</summary>
public interface IStartupManager
{
    /// <summary>True when RecMode is registered to launch at sign-in.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers (to the tray) or unregisters RecMode for launch at sign-in.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>
/// Default <see cref="IStartupManager"/> — a per-user HKCU <c>…\CurrentVersion\Run</c> value (no admin needed).
/// The registered command launches with <c>--tray</c> so sign-in starts RecMode minimized (plan Phase 9/design
/// "Start with Windows → launch minimized to the tray"). This is the one deliberate write outside the portable
/// folder, made only on explicit opt-in.
/// </summary>
public sealed class StartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RecMode";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                string? command = key?.GetValue(ValueName) as string;
                string? current = Environment.ProcessPath;
                return command is not null && current is not null &&
                    string.Equals(ExtractExecutable(command), current, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Optional integration: group policy must not make a portable app crash.
        }
    }

    private static string ExtractExecutable(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : string.Empty;
        }
        int space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}
