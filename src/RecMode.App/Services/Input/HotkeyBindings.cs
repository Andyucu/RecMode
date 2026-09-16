using RecMode.App.ViewModels;
using RecMode.Core.Errors;
using RecMode.Core.Input;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>
/// Binds the global hotkeys (start/stop, pause/resume, screenshot) to the Record view model, reading the
/// chords from settings so they're remappable (plan Phase 9). <see cref="Rebind"/> re-registers after a change.
/// Unparseable/blank settings fall back to the F9/F10/F11 defaults.
/// </summary>
public sealed class HotkeyBindings(GlobalHotkeys hotkeys, RecordViewModel record, ISettingsService settings, IErrorReporter errors) : IDisposable
{
    private int _startStop = -1;
    private int _pause = -1;
    private int _screenshot = -1;
    private int _nextProfile = -1;
    private int _micMute = -1;
    private int _addChapter = -1;
    private bool _hooked;
    private Action<uint>? _onRegistrationFailed;

    public void Register()
    {
        if (!_hooked)
        {
            hotkeys.Pressed += OnPressed;
            // Held in a field so Dispose() can detach it — as a bare lambda it was unremovable, so a disposed
            // HotkeyBindings kept emitting "hotkey.in-use" warnings through the captured error reporter.
            _onRegistrationFailed = _ =>
                errors.Warn("hotkey.in-use", "A global hotkey couldn't be registered (already in use by another app).");
            hotkeys.RegistrationFailed += _onRegistrationFailed;
            _hooked = true;
        }

        RegisterAllFromSettings();
    }

    /// <summary>The one place the action→default-chord table lives. <see cref="Register"/> and
    /// <see cref="Rebind"/> previously each had their own verbatim copy of these five lines, so adding a
    /// sixth hotkey to one and not the other would have been a silent, build-clean bug — in a file that has
    /// already had two hotkey-remap bugs fixed.</summary>
    private void RegisterAllFromSettings()
    {
        RecModeSettings s = settings.Current;
        _nextProfile = Register(s.HotkeyNextProfile, "F8");
        _startStop = Register(s.HotkeyStartStop, "F9");
        _pause = Register(s.HotkeyPauseResume, "F10");
        _screenshot = Register(s.HotkeyScreenshot, "F11");
        _micMute = Register(s.HotkeyMicMute, "Ctrl+Shift+M");
        _addChapter = Register(s.HotkeyAddChapter, "Ctrl+Shift+K");
    }

    /// <summary>Re-registers all hotkeys from the current settings (call after the user remaps one). Each
    /// hotkey is registered independently, same as the initial <see cref="Register"/> at startup — one
    /// action's chord being taken by another app must not block re-registering the other four (a genuinely
    /// unrelated failure here used to make the whole rebind fail and the just-changed hotkey get silently
    /// reverted). A failed registration still surfaces via <see cref="GlobalHotkeys.RegistrationFailed"/>'s
    /// existing "hotkey.in-use" warning. Callers that need to know whether one *specific* chord will register
    /// before committing it to settings should use <see cref="CanRegister"/> instead.</summary>
    public void Rebind()
    {
        hotkeys.UnregisterAll();
        RegisterAllFromSettings();
    }

    /// <summary>True if <paramref name="chord"/> can be registered as a global hotkey right now — probed
    /// without touching any of RecMode's own currently-bound hotkeys and without raising the generic
    /// "hotkey.in-use" warning a real failed registration would (a probe failing is expected, normal
    /// outcome the caller handles itself, not a warning-worthy one). Lets the hotkey-capture UI validate the
    /// specific chord a user is trying to set, decoupled from whether any of RecMode's *other* hotkeys happen
    /// to be taken by an unrelated app at rebind time.</summary>
    public bool CanRegister(HotkeyChord chord) => hotkeys.CanRegister(chord.Modifiers, chord.VirtualKey);

    /// <summary>Unregisters every currently-bound hotkey, for the duration of the hotkey-capture UI listening
    /// for a new chord. Without this, pressing a key combination that happens to already be one of RecMode's
    /// own bound hotkeys (e.g. pressing F9 while trying to remap Screenshot) is delivered to this app as a
    /// <c>WM_HOTKEY</c> message instead of a normal keydown — Windows does not deliver the raw keystroke to
    /// the focused control for a combination that is itself a registered global hotkey, so the capture UI
    /// would never see it and that hotkey's bound action would fire instead. Call <see cref="Resume"/>
    /// (implemented as a plain re-<see cref="Rebind"/>) once capture ends, however it ends.</summary>
    public void Suspend() => hotkeys.UnregisterAll();

    /// <summary>Re-establishes every hotkey from current settings after <see cref="Suspend"/>.</summary>
    public void Resume() => Rebind();

    private int Register(string? chordText, string fallback)
    {
        // Prefer the configured chord; fall back to the built-in default (always valid) if it won't parse.
        if (!HotkeyChord.TryParse(chordText, out HotkeyChord chord) &&
            !HotkeyChord.TryParse(fallback, out chord))
        {
            return -1;
        }

        return hotkeys.Register(chord.Modifiers, chord.VirtualKey);
    }

    private void OnPressed(int id)
    {
        if (id == _startStop)
        {
            if (record.RecordCommand.CanExecute(null)) record.RecordCommand.Execute(null);
        }
        else if (id == _nextProfile)
        {
            record.CycleRecordingProfile();
        }
        else if (id == _pause)
        {
            record.PauseResumeCommand.Execute(null);
        }
        else if (id == _screenshot)
        {
            if (record.ScreenshotCommand.CanExecute(null)) record.ScreenshotCommand.Execute(null);
        }
        else if (id == _micMute)
        {
            record.ToggleMicMuteCommand.Execute(null);
        }
        else if (id == _addChapter)
        {
            record.AddChapterCommand.Execute(null);
        }
    }

    public void Dispose()
    {
        hotkeys.Pressed -= OnPressed;
        if (_onRegistrationFailed is not null)
        {
            hotkeys.RegistrationFailed -= _onRegistrationFailed;
            _onRegistrationFailed = null;
        }
        _hooked = false;
    }
}
