using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RecMode.Core.Errors;
using RecMode.Core.Infrastructure;

namespace RecMode.Core.Settings;

/// <summary>
/// Default <see cref="ISettingsService"/>: JSON file under <c>AppPaths.SettingsFilePath</c>, human-readable
/// string enums, atomic writes, corrupt-file recovery (bad file preserved as <c>.corrupt</c>, defaults
/// loaded, warning reported), and schema migration on load. Debounced saves use a one-shot timer so an
/// idle app has no running timer (plan §3.9).
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(750);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IAppPaths _paths;
    private readonly IErrorReporter _errors;
    private readonly Lock _saveLock = new();
    private Timer? _debounceTimer;
    private bool _disposed;

    public SettingsService(IAppPaths paths, IErrorReporter errors)
    {
        _paths = paths;
        _errors = errors;
        Current = new RecModeSettings();
    }

    public RecModeSettings Current { get; private set; }

    public event EventHandler? SettingsChanged;

    public void Load()
    {
        string path = _paths.SettingsFilePath;
        if (!File.Exists(path))
        {
            Current = new RecModeSettings();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            JsonNode? node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                throw new JsonException("Settings root is not a JSON object.");
            }

            SettingsMigrator.Migrate(obj);
            Current = obj.Deserialize<RecModeSettings>(JsonOptions) ?? new RecModeSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            RecoverCorruptFile(path, ex);
            Current = new RecModeSettings();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        lock (_saveLock)
        {
            CancelDebounce();
            WriteToDisk();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSave()
    {
        lock (_saveLock)
        {
            _debounceTimer ??= new Timer(_ => OnDebounceElapsed(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed()
    {
        lock (_saveLock)
        {
            if (_disposed)
            {
                return;
            }

            WriteToDisk();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WriteToDisk()
    {
        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            Current.SchemaVersion = RecModeSettings.CurrentSchemaVersion;

            string json = JsonSerializer.Serialize(Current, JsonOptions);
            string tempPath = _paths.SettingsFilePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Atomic replace so a crash mid-write never leaves a truncated settings file.
            if (File.Exists(_paths.SettingsFilePath))
            {
                File.Replace(tempPath, _paths.SettingsFilePath, null);
            }
            else
            {
                File.Move(tempPath, _paths.SettingsFilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Warn(
                "settings.save-failed",
                "Couldn't save your settings.",
                "Check that the app folder is writable.",
                ex);
        }
    }

    private void RecoverCorruptFile(string path, Exception cause)
    {
        try
        {
            string backup = path + ".corrupt";
            File.Copy(path, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort; the warning below still fires.
            cause = new AggregateException(cause, ex);
        }

        _errors.Warn(
            "settings.corrupt",
            "Your settings file was unreadable, so defaults were restored.",
            "The unreadable file was kept with a .corrupt extension.",
            cause);
    }

    private void CancelDebounce() => _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose()
    {
        lock (_saveLock)
        {
            _disposed = true;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
