using RecMode.Capture;
using RecMode.Core.Errors;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    private const int MaxRecentTargets = 3;
    private readonly List<CaptureTarget> _recentTargets = [];

    /// <summary>Most-recently-used capture targets (this session only — deliberate v1 scope cut: a
    /// <see cref="CaptureTarget"/>'s handles stay valid for the life of the process, but re-resolving a
    /// Window/Monitor target after a restart would need real staleness handling this doesn't attempt),
    /// newest first, for the tray icon's "Recent" quick-record submenu. Updated whenever a recording actually
    /// starts (see <see cref="StartRecording"/>), not on every source-tile click.</summary>
    public IReadOnlyList<CaptureTarget> RecentTargets => _recentTargets;

    private void RememberRecentTarget(CaptureTarget target) => Remember(_recentTargets, target, MaxRecentTargets);

    /// <summary>
    /// Most-recently-used list maintenance: move-to-front, de-duplicate, cap. Extracted and made
    /// <c>internal static</c> purely so it's testable — the rest of this class needs the full DI-heavy
    /// <see cref="RecordViewModel"/>. Dedup relies on <see cref="CaptureTarget"/> having value equality
    /// (it's a <c>record</c>); the tests pin that, since switching it to a class or adding a mutable field
    /// would silently produce three identical "Display 1" entries in the tray menu with no build error.
    /// </summary>
    internal static void Remember(List<CaptureTarget> recent, CaptureTarget target, int max)
    {
        recent.RemoveAll(t => t.Equals(target));
        recent.Insert(0, target);
        if (recent.Count > max)
        {
            recent.RemoveRange(max, recent.Count - max);
        }
    }

    /// <summary>Tray "Recent" submenu entry point: applies a previously-used target and starts recording
    /// immediately, bypassing the Record screen's UI entirely — works even with no window ever shown
    /// (<c>--tray</c>), same as <see cref="StartRecordingFromCli"/>. No-ops while already recording (matches
    /// every other Record-screen control's behavior of leaving an in-progress recording alone).</summary>
    public void QuickRecordFromTray(CaptureTarget target)
    {
        if (_coordinator.IsRecording || _startInFlight)
        {
            return;
        }

        if (target.Kind == CaptureKind.Window)
        {
            // Refresh the window list first: recent targets are session-scoped HWNDs, and the one the user
            // just clicked may well have been closed since. Without this, a stale handle silently produced no
            // target and StartRecording bailed out — the user clicked a tray menu item and *nothing at all*
            // happened, with no message and no log line.
            LoadWindows();
        }

        ApplyTargetSelection(target);

        if (CurrentTarget() is null)
        {
            _errors.Warn("record.recent-target-gone", "That recording source isn't available any more.",
                "The window may have been closed, or the display disconnected. Pick a source on the Record screen.");
            return;
        }

        StartRecordingFromCli(); // no pre-roll countdown — a tray click means "now," same as CLI automation
    }

    /// <summary>Re-applies a target's source-kind/selection so <see cref="CurrentTarget"/> resolves back to
    /// (an equivalent of) it.</summary>
    private void ApplyTargetSelection(CaptureTarget target)
    {
        switch (target.Kind)
        {
            case CaptureKind.Window:
                SelectedWindow = Windows.FirstOrDefault(w => w.Handle == target.Handle) ?? SelectedWindow;
                break;
            case CaptureKind.Region:
                SelectedMonitor = Monitors.FirstOrDefault(m => m.Handle == target.Handle) ?? SelectedMonitor;
                _region = target.Region;
                break;
            case CaptureKind.Webcam:
                SelectedWebcamDevice = WebcamDevices.FirstOrDefault(d => d.Id == target.WebcamDeviceId) ?? SelectedWebcamDevice;
                break;
            case CaptureKind.AllDisplays:
                SelectedMonitor = Monitors.FirstOrDefault(m => m.IsAllDisplays) ?? SelectedMonitor;
                break;
            default: // Monitor
                SelectedMonitor = Monitors.FirstOrDefault(m => m.Handle == target.Handle) ?? SelectedMonitor;
                break;
        }

        SetSourceKindQuietly(target.Kind);
    }

    /// <summary>
    /// Switches the source-kind flags without the public setters' interactive side effects. Those setters
    /// treat being set as a <em>tile click</em>: <see cref="IsRegionSource"/> in particular pops the modal
    /// full-screen region picker, so routing a tray quick-record through it dimmed the whole screen and
    /// waited for a fresh drag-select instead of recording — the user picked a known past region precisely
    /// so they wouldn't have to draw one again. (<c>RevertToScreen()</c> writes the backing fields directly
    /// for the same reason.) Sets all four flags together so exactly one is ever true, then raises the
    /// notifications the setters would have, so the source tiles and dependent panels still update.
    /// <para>Deliberately does not restart the preview: the only caller starts recording immediately
    /// afterward, which tears the preview down anyway.</para>
    /// </summary>
    private void SetSourceKindQuietly(CaptureKind kind)
    {
        _isScreenSource = kind is CaptureKind.Monitor or CaptureKind.AllDisplays;
        _isWindowSource = kind == CaptureKind.Window;
        _isRegionSource = kind == CaptureKind.Region;
        _isWebcamSource = kind == CaptureKind.Webcam;

        OnPropertyChanged(nameof(IsScreenSource));
        OnPropertyChanged(nameof(IsWindowSource));
        OnPropertyChanged(nameof(IsRegionSource));
        OnPropertyChanged(nameof(IsWebcamSource));
        OnPropertyChanged(nameof(ShowWindowPicker));
        OnPropertyChanged(nameof(ShowFollowWindow));
        OnPropertyChanged(nameof(ShowRegionInfo));
        OnPropertyChanged(nameof(ShowWebcamOverlayCard));
        RecordCommand.NotifyCanExecuteChanged();
    }
}
