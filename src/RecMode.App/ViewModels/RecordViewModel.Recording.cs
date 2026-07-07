using System.Globalization;
using System.IO;
using RecMode.App.Services;
using RecMode.Capture;
using RecMode.Core.Recording;
using RecMode.Encoding.Ffmpeg;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    private void ToggleRecord()
    {
        if (_coordinator.IsRecording)
        {
            _coordinator.Stop();
            return;
        }

        StartRecording(withCountdown: true); // interactive start (button/hotkey/tray) honours the countdown setting
    }

    /// <summary>Starts recording without the pre-roll countdown — for CLI automation (<c>--record</c>), which means "now".</summary>
    public void StartRecordingFromCli()
    {
        if (!_coordinator.IsRecording)
        {
            StartRecording(withCountdown: false);
        }
    }

    private void StartRecording(bool withCountdown)
    {
        CaptureTarget? target = CurrentTarget();
        if (target is null || SelectedEncoder is null)
        {
            return;
        }

        StopPreview(); // preview and recording use separate sessions; don't run both (§3.9)

        if (withCountdown)
        {
            int seconds = _settings.Current.CountdownSeconds;
            MonitorInfo? mon = SelectedMonitor ?? Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
            if (seconds > 0 && mon is not null && !_countdown.Run(mon, seconds))
            {
                StartPreview(); // cancelled during countdown; bring preview back
                return;
            }
        }

        bool started = _coordinator.Start(target, SelectedEncoder, SelectedFormat, SelectedFrameRate, Quality);
        if (started)
        {
            IsRecording = true;
            StatusText = "Recording";
            StatsText = "";
        }
        else
        {
            StartPreview(); // start failed; bring preview back
        }
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
        ElapsedText = "00:00";
        StatusText = result.Success
            ? $"Saved {Path.GetFileName(result.OutputPath)}"
            : "Recording ended — check the log";
        StatsText = "";
        UpdateDiskSpaceText(); // back to the "free of total" idle view
        StartPreview(); // resume the live preview
    });
}
