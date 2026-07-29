using System.IO;
using Microsoft.Win32;

namespace RecMode.App.Services;

/// <summary>Reads/writes the "start with Windows" state. A labelled §3.5 opt-in exception to portable containment.</summary>
public interface IStartupManager
{
    /// <summary>True when RecMode is registered to launch at sign-in.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers (to the tray) or unregisters RecMode for launch at sign-in.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Self-heals a Run-key entry left pointing at a portable install that's since moved. Call once
    /// at every launch. <paramref name="userOptedIntoStartup"/> must be this install's own persisted
    /// "start with Windows" setting — reconciliation only ever touches the key when this install itself opted
    /// in, so a copy that never enabled the setting can never silently adopt someone else's stale entry.</summary>
    void ReconcileAfterMove(bool userOptedIntoStartup);
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

    /// <summary>The Run key is the one thing this portable app writes outside its own folder (§3.5), and it's
    /// self-referential in a way nothing else in the app is: if the user moves or re-extracts the portable
    /// folder somewhere else, the registry value keeps pointing at the old, now-gone location forever — there
    /// was previously no code path that ever revisited it. <see cref="IsEnabled"/> already reports this
    /// correctly as "off" (the registered exe doesn't match the current one), so the Settings toggle doesn't
    /// lie, but the stale entry itself just sits there, silently failing to launch anything at every future
    /// sign-in. Called once per launch: if a registered path no longer exists on disk at all — unambiguous
    /// proof the folder moved, not just a coincidentally different install — re-points the entry at the
    /// current exe instead, preserving what was almost certainly still-wanted "start with Windows" intent
    /// rather than leaving a dead reference or silently turning the feature off.
    /// <para>
    /// Gated on <paramref name="userOptedIntoStartup"/>: without it, a copy of RecMode that has never itself
    /// opted into "start with Windows" — extracted once just to try it, run, and later deleted — could silently
    /// adopt an existing stale Run-key entry left by a different install the moment that install's own exe
    /// stopped existing on disk (e.g. that other copy was itself deleted), registering itself to autostart with
    /// no prompt, no UI feedback, and no consent from whoever ran it. Requiring this install's own persisted
    /// opt-in means reconciliation only ever repoints an entry *this* install's user actually asked for.
    /// </para></summary>
    public void ReconcileAfterMove(bool userOptedIntoStartup)
    {
        if (!userOptedIntoStartup)
        {
            return;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            string? command = key?.GetValue(ValueName) as string;
            string? current = Environment.ProcessPath;
            if (command is null || current is null)
            {
                return;
            }

            string registeredExe = ExtractExecutable(command);
            if (string.Equals(registeredExe, current, StringComparison.OrdinalIgnoreCase))
            {
                return; // already correct
            }

            if (!File.Exists(registeredExe))
            {
                key!.SetValue(ValueName, $"\"{current}\" --tray");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Best-effort, same failure tolerance as IsEnabled/SetEnabled.
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
