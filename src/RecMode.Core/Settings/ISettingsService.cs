namespace RecMode.Core.Settings;

/// <summary>
/// Loads, exposes, and persists <see cref="RecModeSettings"/> (plan §3.2). Saves are debounced; the
/// current instance is the live document that the app mutates and observes.
/// </summary>
public interface ISettingsService
{
    /// <summary>The live settings document. Mutate it, then call <see cref="Save"/> (or <see cref="RequestSave"/>).</summary>
    RecModeSettings Current { get; }

    /// <summary>Raised after settings are (re)loaded or saved, so observers can refresh.</summary>
    event EventHandler? SettingsChanged;

    /// <summary>Loads from disk, running migrations and recovering from a corrupt file. Never throws for bad content.</summary>
    void Load();

    /// <summary>Writes the current settings to disk immediately.</summary>
    void Save();

    /// <summary>Requests a debounced save (coalesces rapid changes into one write).</summary>
    void RequestSave();
}
