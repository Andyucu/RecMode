using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Recording;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    // Guards against a re-entrant start: CountdownController.Run() shows a modal window (ShowDialog()),
    // which pumps messages while the pre-roll countdown is up — including the WM_HOTKEY that delivers a
    // second F9 press. _coordinator.IsRecording is still false throughout that whole window (recording only
    // actually starts once the countdown finishes), so without this a second press during the countdown (or
    // a rapid double-click of the tray "Start/stop" item) would re-enter StartRecording and open a second
    // countdown on top of the first, ultimately calling _coordinator.Start(...) twice.
    private bool _startInFlight;

    private void ToggleRecord()
    {
        if (_coordinator.IsRecording)
        {
            // RecordingCoordinator.Stop() can block for tens of seconds in the worst case: it joins the
            // pacer thread with no timeout, which can itself be deep inside an auto-split segment's
            // finalize + safe-recording remux + encoder-restart sequence right when the user clicks Stop.
            // Run it off the UI thread so the window (and its message pump — hotkeys, tray, everything)
            // stays responsive instead of going "not responding" for however long that takes.
            // RecordingCoordinator.Finished already marshals onto the dispatcher (see Dispatch() in
            // OnFinished below), so IsRecording/StatusText update correctly once Stop() actually
            // completes — only *when* the blocking happens has changed, not the eventual outcome. Stop()'s
            // own TryClaimFinalize/_finalizationCompleted guards already handle a second concurrent Stop()
            // call safely (a rapid double-click just blocks the second background Task instead of the UI).
            System.Threading.Tasks.Task.Run(() => _coordinator.Stop());
            return;
        }

        if (_startInFlight)
        {
            return; // already starting (e.g. mid pre-roll countdown) — ignore the extra press
        }

        _ = StartRecording(withCountdown: true); // interactive start (button/hotkey/tray) honours the countdown setting
    }

    /// <summary>Starts recording without the pre-roll countdown — for CLI automation (<c>--record</c>), which
    /// means "now". Returns whether the recording actually started, once it has (not merely been kicked off) —
    /// <see cref="RecordingCoordinator.Start"/> itself runs on a background thread, so a caller that checked
    /// <see cref="IsRecording"/> synchronously right after this method returned would always see it still
    /// false: pre-flight, webcam activation, and encoder startup ("can take several seconds", per
    /// <see cref="StartRecording"/>'s own comment) all happen before the state machine transitions. This is
    /// what <see cref="Services.SchedulerService.Fire"/> awaits instead.</summary>
    public Task<bool> StartRecordingFromCli()
    {
        if (_coordinator.IsRecording || _startInFlight)
        {
            return Task.FromResult(false);
        }

        return StartRecording(withCountdown: false);
    }

    private Task<bool> StartRecording(bool withCountdown)
    {
        CaptureTarget? target = CurrentTarget();
        if (target is null || SelectedEncoder is null)
        {
            return Task.FromResult(false);
        }

        _startInFlight = true;
        StopPreview(); // preview and recording use separate sessions; don't run both (§3.9)

        if (withCountdown)
        {
            int seconds = _settings.Current.CountdownSeconds;
            MonitorInfo? mon = SelectedMonitor ?? Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
            if (seconds > 0 && mon is not null && !_countdown.Run(mon, seconds))
            {
                _startInFlight = false;
                StartPreview(); // cancelled during countdown; bring preview back
                return Task.FromResult(false);
            }
        }

        // RecordingCoordinator.Start() runs off the dispatcher. It does the whole pre-flight — including an
        // 8 MB write-through disk-speed probe that takes seconds on a mapped network share — can activate a
        // webcam synchronously for the picture-in-picture overlay, and then walks the encoder fallback chain,
        // where its own comments note that encoder startup "can take several seconds". On the UI thread that
        // froze the window, the tray menu, and the global hotkeys for the whole duration, and left the
        // pre-roll countdown unable to repaint. This is the symmetric partner to the Stop() fix (0.9.66):
        // Start() blocks for the same reasons and was simply never moved. Start() catches internally and
        // returns false rather than throwing, so no exception escapes the task.
        EncoderInfo encoder = SelectedEncoder;
        MediaContainer container = SelectedFormat;
        int fps = SelectedFrameRate;
        int quality = Quality;
        StatusText = Resources.Strings.Record_StatusStarting;

        // The returned task completes with the real outcome as soon as _coordinator.Start() itself returns —
        // deliberately not chained after the UI-thread dispatch below, which is fire-and-forget via
        // BeginInvoke and could complete anywhere from immediately to several message-loop turns later. A
        // caller that needs to know "did the recording actually start" (SchedulerService.Fire, in particular)
        // awaits this directly rather than racing that dispatch.
        Task<bool> startTask = System.Threading.Tasks.Task.Run(() => _coordinator.Start(target, encoder, container, fps, quality));

        _ = startTask.ContinueWith(
                t => Dispatch(() =>
                {
                    // _startInFlight stays true across the whole async start, so a second F9/tray click
                    // during it is still ignored — same re-entrancy guard as before, just held longer.
                    try
                    {
                        if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && t.Result)
                        {
                            ActiveCaptureTarget = target;
                            RememberRecentTarget(target);
                            IsRecording = true;
                            StatusText = Resources.Strings.Record_StatusRecording;
                            StatsText = "";
                            LastRecordingPath = null; // the title-bar "jump to Library" link only points at a finished recording
                        }
                        else
                        {
                            StatusText = Resources.Strings.Record_StatusReady;
                            StartPreview(); // start failed; bring preview back
                        }
                    }
                    finally
                    {
                        _startInFlight = false;
                    }
                }),
                System.Threading.Tasks.TaskScheduler.Default);

        return startTask;
    }

    private void TogglePause()
    {
        if (_coordinator.IsPaused)
        {
            _coordinator.Resume();
        }
        else
        {
            _coordinator.Pause();
        }
    }

    private void OnProgress(RecordingProgress p) => Dispatch(() =>
    {
        IsRecording = p.State is not Core.Recording.RecordingState.Idle and not Core.Recording.RecordingState.Finalizing;
        IsPaused = p.State == Core.Recording.RecordingState.Paused;
        IsHealthy = p.IsHealthy;
        ElapsedText = FormatElapsed(p.Elapsed);
        string stats = $"{p.Fps.ToString("F0", CultureInfo.InvariantCulture)} fps · {p.Mbps.ToString("F1", CultureInfo.InvariantCulture)} Mbps · {FormatBytes(p.FileSizeBytes)}";
        StatsText = IsPaused ? "Paused" : p.IsHealthy ? stats : $"⚠ Can't keep up · {stats}";
        UpdateDiskSpaceText(p.FileSizeBytes);
    });

    private void OnFinished(RecordingResult result) => Dispatch(() =>
    {
        IsRecording = false;
        IsHealthy = true;
        IsAnnotating = false;
        IsManualZooming = false; // no coordinator.SetZoomTarget call needed — capture already stopped
        IsMicMuted = false;
        ActiveCaptureTarget = null;
        ElapsedText = "00:00";
        StatusText = result.Success
            ? $"Saved {Path.GetFileName(result.OutputPath)}"
            : "Recording ended — check the log";
        LastRecordingPath = result.Success ? result.OutputPath : null;
        StatsText = "";
        UpdateDiskSpaceText(); // back to the "free of total" idle view
        StartPreview(); // resume the live preview
    });
}
