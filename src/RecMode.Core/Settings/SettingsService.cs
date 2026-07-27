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
    private readonly SynchronizationContext? _notificationContext;
    private readonly Lock _saveLock = new();
    private Timer? _debounceTimer;
    private bool _savePending;
    private bool _disposed;

    public SettingsService(IAppPaths paths, IErrorReporter errors)
    {
        _paths = paths;
        _errors = errors;
        // Services are composed on the WPF thread.  Keeping this dependency-free lets core tests run
        // without a dispatcher while ensuring UI subscribers never receive a timer-thread notification.
        _notificationContext = SynchronizationContext.Current;
        Current = new RecModeSettings();
    }

    public RecModeSettings Current { get; private set; }

    public bool IsFirstRun { get; private set; }

    public event EventHandler? SettingsChanged;

    public void Load()
    {
        string path = _paths.SettingsFilePath;
        if (!File.Exists(path))
        {
            IsFirstRun = true;
            Current = new RecModeSettings();
            RaiseSettingsChanged();
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

        RaiseSettingsChanged();
    }

    public void Save()
    {
        lock (_saveLock)
        {
            CancelDebounce();
            _savePending = false;
            try
            {
                WriteToDisk(CreateSnapshot());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Same race FlushDebouncedSave already guards against: CreateSnapshot() enumerates
                // Current.Schedules/CustomProfiles, which can throw InvalidOperationException if a view
                // model mutates either list concurrently (e.g. SchedulerService.Fire() calling Save() from
                // its own tick at the same moment the user edits a Schedule row). Save() previously had no
                // catch here at all — an unhandled exception on whichever caller happened to trigger the race.
                _errors.Warn("settings.snapshot-failed", "Couldn't prepare your settings for saving.", null, ex);
                return;
            }
        }

        RaiseSettingsChanged();
    }

    /// <summary>Requests a debounced save. Only (re)arms the timer — does <em>not</em> snapshot <see cref="Current"/>
    /// here. A dragged slider fires this on every tick (up to ~100 times for one drag), and the old behavior of
    /// serializing the whole settings object (all schedules, all custom profiles) synchronously on every single
    /// call violated §3.9's "allocation-free/throttled hot paths" for no benefit — only the debounce's eventual
    /// single flush ever reaches disk. The snapshot itself happens once, when the debounce actually elapses —
    /// see <see cref="OnDebounceElapsed"/> for why that still has to happen on the UI thread, not the timer's
    /// own thread pool thread.</summary>
    public void RequestSave()
    {
        lock (_saveLock)
        {
            if (_disposed)
            {
                return;
            }

            _savePending = true;
            _debounceTimer ??= new Timer(_ => OnDebounceElapsed(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed()
    {
        // Fires on a thread-pool timer thread. CreateSnapshot() enumerates Current.Schedules/CustomProfiles,
        // which view models only ever mutate from the UI thread — serializing them concurrently from here
        // would race exactly like Save()/Dispose() already can (a known, separate gap). Marshal back onto the
        // same UI SynchronizationContext RaiseSettingsChanged already uses, so the actual snapshot+write always
        // happens on the same thread as every other settings mutation, same as the old synchronous-in-RequestSave
        // behavior did — just deferred to once per debounce window instead of once per call.
        if (_notificationContext is not null && SynchronizationContext.Current != _notificationContext)
        {
            _notificationContext.Post(_ => FlushDebouncedSave(), null);
        }
        else
        {
            FlushDebouncedSave();
        }
    }

    private void FlushDebouncedSave()
    {
        lock (_saveLock)
        {
            if (_disposed || !_savePending)
            {
                // An immediate Save() (or a second debounce window's own flush) already handled this,
                // or the service was disposed (Dispose() flushes any pending save itself) before this
                // marshaled callback got to run.
                return;
            }

            try
            {
                WriteToDisk(CreateSnapshot());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                _errors.Warn("settings.snapshot-failed", "Couldn't prepare your settings for saving.", null, ex);
            }
            _savePending = false;
        }

        RaiseSettingsChanged();
    }

    private string CreateSnapshot()
    {
        // Only ever raise the recorded schema version, never lower it. A file written by a newer build keeps
        // its own higher number so that build's migration steps don't re-run against already-migrated data
        // (SettingsMigrator makes the same choice on the way in). Combined with
        // RecModeSettings.UnknownProperties, this is what lets an older build read and re-save a newer
        // build's settings without destroying them.
        if (Current.SchemaVersion < RecModeSettings.CurrentSchemaVersion)
        {
            Current.SchemaVersion = RecModeSettings.CurrentSchemaVersion;
        }

        return JsonSerializer.Serialize(Current, JsonOptions);
    }

    private void WriteToDisk(string json)
    {
        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            // Atomic (temp-file + rename, flushed to physical disk first) so a crash mid-write never leaves
            // a truncated settings file.
            AtomicFileWriter.Write(_paths.SettingsFilePath, json);
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

    private void RaiseSettingsChanged()
    {
        EventHandler? handler = SettingsChanged;
        if (handler is null) return;
        if (_notificationContext is null || SynchronizationContext.Current == _notificationContext)
        {
            handler(this, EventArgs.Empty);
            return;
        }

        _notificationContext.Post(_ => handler(this, EventArgs.Empty), null);
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
            // A debounced write must not be lost merely because the app is closing before its timer fires.
            if (_savePending)
            {
                CancelDebounce();
                try
                {
                    WriteToDisk(CreateSnapshot());
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    // Same race as Save()/FlushDebouncedSave — must not throw out of Dispose() during shutdown.
                    _errors.Warn("settings.snapshot-failed", "Couldn't prepare your settings for saving.", null, ex);
                }
                _savePending = false;
            }

            _disposed = true;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
