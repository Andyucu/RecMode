using System.Windows.Threading;
using RecMode.App.ViewModels;
using RecMode.Core.Settings;
using RecMode.Encoding.Ffmpeg;
using Serilog;

namespace RecMode.App.Services;

/// <summary>
/// Fires scheduled recordings (plan Phase 8). A ~20 s poll checks each schedule via the pure
/// <see cref="ScheduleEvaluator"/>; a due one starts a recording with the current Record settings and is
/// stopped after its duration. Runs while the app is alive (including from the tray); missed windows (app
/// closed) aren't caught up. Never interrupts an in-progress recording.
///
/// <para><b>Deliberate §3.9 exception, not an oversight:</b> this is the one place in the app that polls
/// on a fixed interval rather than being purely event-driven. Schedules match at minute granularity
/// (<see cref="ScheduleEvaluator.IsDue"/> checks <c>now.Hour</c>/<c>now.Minute</c>), and this timer runs at
/// <see cref="DispatcherPriority.Background"/> — a single tick could be delayed past its target minute if
/// the UI thread is busy with higher-priority work, silently skipping that occurrence entirely (a missed
/// "Once" schedule never re-fires; Daily/Weekly slip a full cycle). Polling ~3×/minute is the fault-tolerance
/// margin against exactly that: if one tick is late, another within the same minute still catches it. The
/// cost (one wakeup per 20s while idle-in-tray) is negligible, so the tradeoff favors correctness over
/// purity here rather than a precisely-armed single-shot timer.</para>
/// </summary>
public sealed class SchedulerService(ISettingsService settings, RecordViewModel record, RecordingCoordinator coordinator) : IDisposable
{
    private DispatcherTimer? _timer;
    private DateTimeOffset? _scheduledStopAt;
    private bool _scheduledRecordingActive;
    private bool _started;

    // RecordViewModel.StartRecordingFromCli() now runs pre-flight/encoder-startup on a background thread and
    // returns a Task<bool> that only completes once a recording has actually started (or failed) — often
    // several seconds later. Without this guard, a Fire() whose Task hasn't resolved yet left every check in
    // Tick() (coordinator.IsRecording in particular) still reading false, so the next ~20s tick would see the
    // same schedule as still due and fire it a second time before the first attempt had even finished.
    private bool _fireInFlight;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        coordinator.Finished += OnRecordingFinished;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick(); // Do not miss a schedule when the app opens during its matching minute.
    }

    private void Tick()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        // Stop a scheduled recording that has run its duration.
        if (_scheduledRecordingActive && _scheduledStopAt is { } stopAt && now >= stopAt)
        {
            if (coordinator.IsRecording)
            {
                Log.Information("Scheduled recording reached its duration — stopping");
                // RecordingCoordinator.Stop() can block for tens of seconds in the worst case (an unbounded
                // pacer-thread join that can be deep inside an auto-split segment's finalize/remux/encoder-
                // restart sequence) — the same reason RecordViewModel.ToggleRecord() runs it off the UI
                // thread instead of calling it inline. This Tick() runs on a DispatcherTimer, so calling
                // Stop() directly here would freeze the whole app for however long that takes, for an
                // unattended scheduled recording nobody is even watching end. coordinator.Finished (observed
                // by OnRecordingFinished below, and separately by RecordViewModel's own dispatcher-marshaled
                // handler) still fires correctly once Stop() actually completes, just from a background
                // thread instead of this one — already a safe, established pattern (see ToggleRecord).
                System.Threading.Tasks.Task.Run(coordinator.Stop);
            }

            return; // let the next tick evaluate a fresh state
        }

        if (coordinator.IsRecording || _fireInFlight)
        {
            return; // never interrupt an in-progress (manual or scheduled) recording, or double-fire mid-start
        }

        foreach (ScheduleItem item in settings.Current.Schedules)
        {
            if (ScheduleEvaluator.IsDue(item, now))
            {
                _fireInFlight = true;
                _ = Fire(item, now); // fire-and-forget from this tick; Tick() must not block on encoder startup
                break; // one at a time
            }
        }
    }

    private async System.Threading.Tasks.Task Fire(ScheduleItem item, DateTimeOffset now)
    {
        Log.Information("Firing schedule {Name} ({Recurrence} @ {Time}, {Dur} min)",
            item.Name, item.Recurrence, item.Time, item.DurationMinutes);

        // Bound the profile's lifetime to cover the *actual* async start, not just the synchronous kick-off —
        // otherwise ApplyProfileForSchedule's restore (in the IDisposable returned below) ran microseconds
        // after Task.Run was queued and always won the race against RecordingCoordinator.Start() reading
        // audio settings from _settings.Current on its own background thread, silently recording with the
        // Record screen's audio state instead of the schedule-bound profile's.
        IDisposable? scheduledProfile = null;
        if (item.ProfileName is not null)
        {
            RecordingProfile? profile = record.Profiles.FirstOrDefault(p => p.Name == item.ProfileName);
            if (profile is not null)
            {
                scheduledProfile = record.ApplyProfileForSchedule(profile);
            }
            else
            {
                Log.Warning("Schedule {Name} references profile \"{Profile}\" which no longer exists — using current Record settings",
                    item.Name, item.ProfileName);
            }
        }

        bool started;
        try
        {
            record.EnsureDevicesLoaded();
            started = await record.StartRecordingFromCli().ConfigureAwait(true);
        }
        finally
        {
            scheduledProfile?.Dispose();
            _fireInFlight = false;
        }

        if (started)
        {
            // Mark success only after a recording has actually started. A broken target/output remains visible
            // and can be retried after the user fixes it rather than silently disabling a one-time schedule.
            item.LastFiredUtc = now;
            _ = TimeOnly.TryParse(item.Time, out TimeOnly scheduledTime);
            item.LastFiredOccurrence = ScheduleEvaluator.OccurrenceKey(now, scheduledTime);
            if (item.Recurrence == ScheduleRecurrence.Once) item.Enabled = false;
            settings.Save();
            _scheduledRecordingActive = true;
            // Anchored to DateTimeOffset.Now (captured HERE, once recording has actually started), not to the
            // tick's own `now` — that timestamp precedes the whole await above, which covers pre-flight
            // (including an 8 MB disk-speed probe that "takes seconds on a mapped network share", per this
            // method's own comment) and the encoder fallback chain. Anchoring to the tick time under-ran a
            // schedule's duration by however long that startup took — a 5-minute schedule on a slow output
            // could record only ~4:50, silently, with no indication the duration was ever wrong. `now` above
            // is deliberately still used for LastFiredUtc/LastFiredOccurrence, which are occurrence identity
            // (which scheduled slot fired), not duration.
            _scheduledStopAt = DateTimeOffset.Now.AddMinutes(Math.Max(1, item.DurationMinutes));
        }
        else
        {
            Log.Warning("Schedule {Name} fired but the recording didn't start (no source/encoder?)", item.Name);
        }
    }

    private void OnRecordingFinished(RecordingResult _)
    {
        // This only tracks the session this scheduler started. Once it finishes, a later manual recording
        // must never inherit this schedule's expiry.
        if (_scheduledRecordingActive)
        {
            _scheduledRecordingActive = false;
            _scheduledStopAt = null;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        if (_started)
        {
            coordinator.Finished -= OnRecordingFinished;
            _started = false;
        }
    }
}
