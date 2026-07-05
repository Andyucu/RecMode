using System.Windows.Threading;
using RecMode.App.ViewModels;
using RecMode.Core.Settings;
using Serilog;

namespace RecMode.App.Services;

/// <summary>
/// Fires scheduled recordings (plan Phase 8). A ~20 s poll checks each schedule via the pure
/// <see cref="ScheduleEvaluator"/>; a due one starts a recording with the current Record settings and is
/// stopped after its duration. Runs while the app is alive (including from the tray); missed windows (app
/// closed) aren't caught up. Never interrupts an in-progress recording.
/// </summary>
public sealed class SchedulerService(ISettingsService settings, RecordViewModel record, RecordingCoordinator coordinator) : IDisposable
{
    private DispatcherTimer? _timer;
    private DateTimeOffset? _scheduledStopAt;

    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        // Stop a scheduled recording that has run its duration.
        if (_scheduledStopAt is { } stopAt && now >= stopAt)
        {
            _scheduledStopAt = null;
            if (coordinator.IsRecording)
            {
                Log.Information("Scheduled recording reached its duration — stopping");
                coordinator.Stop();
            }

            return; // let the next tick evaluate a fresh state
        }

        if (coordinator.IsRecording)
        {
            return; // never interrupt an in-progress (manual or scheduled) recording
        }

        foreach (ScheduleItem item in settings.Current.Schedules)
        {
            if (ScheduleEvaluator.IsDue(item, now))
            {
                Fire(item, now);
                break; // one at a time
            }
        }
    }

    private void Fire(ScheduleItem item, DateTimeOffset now)
    {
        // Mark fired first (dedup): a failed start shouldn't hammer-retry every poll within the minute.
        item.LastFiredUtc = now;
        if (item.Recurrence == ScheduleRecurrence.Once)
        {
            item.Enabled = false;
        }

        settings.RequestSave();

        Log.Information("Firing schedule {Name} ({Recurrence} @ {Time}, {Dur} min)",
            item.Name, item.Recurrence, item.Time, item.DurationMinutes);

        record.EnsureDevicesLoaded();
        record.StartRecordingFromCli();

        if (coordinator.IsRecording)
        {
            _scheduledStopAt = now.AddMinutes(Math.Max(1, item.DurationMinutes));
        }
        else
        {
            Log.Warning("Schedule {Name} fired but the recording didn't start (no source/encoder?)", item.Name);
        }
    }

    public void Dispose() => _timer?.Stop();
}
