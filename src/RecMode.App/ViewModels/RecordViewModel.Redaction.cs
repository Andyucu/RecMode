using CommunityToolkit.Mvvm.Input;
using RecMode.App.Resources;
using RecMode.Capture;
using RecMode.Core.Errors;

namespace RecMode.App.ViewModels;

/// <summary>Live redaction (plan §7 privacy): a marked rectangle is blanked out in the captured frames, so
/// it never reaches the encoder. The rect is chosen with the existing region-picker overlay and stored in
/// absolute screen pixels; it is mapped onto whichever source is selected whenever the preview starts or the
/// setting changes. Whether it can actually be applied is <c>RecordingCoordinator</c>'s call — it refuses to
/// record when it can't, rather than producing an unredacted file.</summary>
public sealed partial class RecordViewModel
{
    private bool _redactAreaEnabled;
    private RegionRect? _redactArea;

    /// <summary>Opens the "mark the area to blank out" picker.</summary>
    public IRelayCommand ChooseRedactAreaCommand { get; }

    /// <summary>Forgets the marked area and turns redaction off.</summary>
    public IRelayCommand ClearRedactAreaCommand { get; }

    /// <summary>Floating-toolbar toggle: arms/clears redaction <em>during</em> a recording, for the area
    /// already marked on the Record screen.</summary>
    public IRelayCommand ToggleRedactionCommand { get; }

    private bool _isRedacting;

    /// <summary>Whether redaction is armed for the recording in progress. Mirrors the toolbar button's lit
    /// state; deliberately a separate flag from <see cref="RedactAreaEnabled"/>, which is the pre-recording
    /// preference.</summary>
    public bool IsRedacting
    {
        get => _isRedacting;
        set => SetProperty(ref _isRedacting, value);
    }

    /// <summary>The toolbar's redact button is offered only when an area is marked and the active capture can
    /// actually blank it. When false the button is disabled (or hidden if nothing is marked at all) rather
    /// than arming a no-op.</summary>
    public bool CanToggleRedaction => HasRedactArea && _coordinator.CaptureSupportsRedaction;

    /// <summary>Mid-recording arm/clear from the floating toolbar. Enabling is verified: if the capture can't
    /// apply it (no GPU pass, or the marked area doesn't overlap the recording) the toggle is left off and the
    /// user is warned, so the button can never indicate "hidden" while nothing is hidden.</summary>
    private void ToggleRedaction()
    {
        if (!_coordinator.IsRecording)
        {
            return;
        }

        if (IsRedacting)
        {
            _coordinator.SetRedaction(null);
            IsRedacting = false;
            return;
        }

        RegionRect? rect = CurrentRedactionSourceRect();
        if (rect is null || !_coordinator.SetRedaction(rect))
        {
            _errors.Warn("record.redaction-unavailable",
                "The marked area couldn't be redacted.",
                "It has to overlap what's being recorded on a GPU capture. The recording continues unredacted.");
            return;
        }

        IsRedacting = true;
    }

    public bool RedactAreaEnabled
    {
        get => _redactAreaEnabled;
        set
        {
            if (!SetProperty(ref _redactAreaEnabled, value))
            {
                return;
            }

            if (value && _redactArea is null)
            {
                // Nothing marked yet — ask for an area instead of arming a toggle that would do nothing and
                // then block the next recording (the coordinator treats "enabled but ineffective" as fatal).
                _redactAreaEnabled = false;
                OnPropertyChanged(nameof(RedactAreaEnabled));
                ChooseRedactArea();
                return;
            }

            _settings.Current.RedactAreaEnabled = value;
            _settings.RequestSave();
            PushRedaction();
        }
    }

    public bool HasRedactArea => _redactArea is not null;

    public string RedactAreaLabel => _redactArea is { } r
        ? $"{r.Width} × {r.Height}  ({r.X}, {r.Y})"
        : Strings.Record_RedactAreaNone;

    /// <summary>Opens the region-picker overlay to mark the area to blank out. Uses the selected monitor
    /// (like the region source's own picker); the marked rect is stored in screen pixels and mapped onto the
    /// capture source when armed.</summary>
    private void ChooseRedactArea()
    {
        MonitorInfo? mon = SelectedMonitor is { IsAllDisplays: false } m
            ? m
            : Monitors.FirstOrDefault(x => x.IsPrimary && !x.IsAllDisplays);
        if (mon is null)
        {
            return;
        }

        _selectingRegion = true;
        IsModalPromptOpen = true;
        RegionRect? picked;
        try
        {
            picked = _regionPicker.Pick(mon);
        }
        finally
        {
            _selectingRegion = false;
            IsModalPromptOpen = false;
        }

        if (picked is not { } r)
        {
            return; // cancelled — leave everything as it was
        }

        _redactArea = r;
        _settings.Current.RedactAreaX = r.X;
        _settings.Current.RedactAreaY = r.Y;
        _settings.Current.RedactAreaWidth = r.Width;
        _settings.Current.RedactAreaHeight = r.Height;
        _redactAreaEnabled = true;
        _settings.Current.RedactAreaEnabled = true;
        _settings.RequestSave();

        OnPropertyChanged(nameof(RedactAreaEnabled));
        OnPropertyChanged(nameof(HasRedactArea));
        OnPropertyChanged(nameof(RedactAreaLabel));
        OnPropertyChanged(nameof(CanToggleRedaction));
        ClearRedactAreaCommand.NotifyCanExecuteChanged();
        PushRedaction();
    }

    private void ClearRedactArea()
    {
        _redactArea = null;
        _redactAreaEnabled = false;
        _settings.Current.RedactAreaEnabled = false;
        _settings.Current.RedactAreaWidth = 0;
        _settings.Current.RedactAreaHeight = 0;
        _settings.RequestSave();

        OnPropertyChanged(nameof(RedactAreaEnabled));
        OnPropertyChanged(nameof(HasRedactArea));
        OnPropertyChanged(nameof(RedactAreaLabel));
        OnPropertyChanged(nameof(CanToggleRedaction));
        ClearRedactAreaCommand.NotifyCanExecuteChanged();
        PushRedaction();
    }

    /// <summary>Pushes the current rect to the live preview so the user sees exactly what will be blanked.
    /// Deliberately preview-only: the recording's own redaction is armed once at start (while the UI controls
    /// are disabled), which keeps "requested but not applied" from ever arising mid-recording.</summary>
    private void PushRedaction() => _preview?.SetRedaction(CurrentRedactionSourceRect());

    /// <summary>Re-reads the marked area from settings. The Settings screen's Privacy card edits the same
    /// fields, so navigating back to Record has to pick those up rather than trust the constructor-time
    /// snapshot. Skipped while recording — the live state is authoritative then.</summary>
    private void RefreshRedactionFromSettings()
    {
        if (_coordinator.IsRecording)
        {
            return;
        }

        var s = _settings.Current;
        RegionRect? rect = s.RedactAreaWidth > 0 && s.RedactAreaHeight > 0
            ? new RegionRect(s.RedactAreaX, s.RedactAreaY, s.RedactAreaWidth, s.RedactAreaHeight)
            : null;
        bool enabled = s.RedactAreaEnabled && rect is not null;
        bool changed = !Nullable.Equals(_redactArea, rect) || _redactAreaEnabled != enabled;

        _redactArea = rect;
        _redactAreaEnabled = enabled;

        OnPropertyChanged(nameof(RedactAreaEnabled));
        OnPropertyChanged(nameof(HasRedactArea));
        OnPropertyChanged(nameof(RedactAreaLabel));
        OnPropertyChanged(nameof(CanToggleRedaction));
        ClearRedactAreaCommand.NotifyCanExecuteChanged();
        if (changed)
        {
            PushRedaction();
        }
    }

    /// <summary>The marked rect in the current capture target's own pixel space, or null when redaction is
    /// off, no area is marked, or the target has no resolvable screen bounds (webcam source).</summary>
    private RegionRect? CurrentRedactionSourceRect()
    {
        if (!_redactAreaEnabled || _redactArea is not { } r)
        {
            return null;
        }

        CaptureTarget? target = CurrentTarget(refreshFollowedWindow: false);
        if (target is null || !CaptureCapabilities.TryGetTextureBounds(target, out RegionRect bounds))
        {
            return null;
        }

        return new RegionRect(r.X - bounds.X, r.Y - bounds.Y, r.Width, r.Height);
    }
}
