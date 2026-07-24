using System.ComponentModel;
using RecMode.App.ViewModels;

namespace RecMode.App.Services;

/// <summary>
/// Auto-pause safety guard: pauses the active recording when the workstation locks (<see cref="SessionLockMonitor"/>
/// — nothing useful is on screen to record past that point, and continuing to encode while stepped away just
/// burns CPU/disk). Installs the session-lock hook only while actually recording (§3.9), mirroring
/// <see cref="ClickHighlightService"/>'s install/uninstall-on-<c>IsRecording</c> pattern. The low-disk-space
/// half of this guard lives directly in <see cref="RecordingCoordinator"/>'s existing pace-loop disk check
/// (which used to stop the recording outright on &lt;500MB free; it now pauses instead, so a recording can
/// resume once space is freed rather than being force-finalized).
/// <para>Deliberately pause-only, not auto-resume-on-unlock: resuming is left to the user (the existing
/// Pause/Resume UI), same as the disk-space guard — an auto-pause that silently starts recording again on its
/// own is a bigger surprise than one that stays paused until the user says otherwise.</para>
/// </summary>
public sealed class AutoPauseGuardService(RecordViewModel record, RecordingCoordinator coordinator, SessionLockMonitor lockMonitor) : IDisposable
{
    private bool _installed;

    public void Attach()
    {
        record.PropertyChanged += OnPropertyChanged;
        Update();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordViewModel.IsRecording))
        {
            Update();
        }
    }

    private void Update()
    {
        if (record.IsRecording && !_installed)
        {
            lockMonitor.Locked += OnLocked;
            lockMonitor.Install();
            _installed = true;
        }
        else if (!record.IsRecording && _installed)
        {
            lockMonitor.Uninstall();
            lockMonitor.Locked -= OnLocked;
            _installed = false;
        }
    }

    private void OnLocked() => coordinator.Pause();

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        if (_installed)
        {
            lockMonitor.Uninstall();
            lockMonitor.Locked -= OnLocked;
            _installed = false;
        }
    }
}
