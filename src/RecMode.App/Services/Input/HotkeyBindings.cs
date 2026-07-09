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
    private bool _hooked;

    public void Register()
    {
        if (!_hooked)
        {
            hotkeys.Pressed += OnPressed;
            hotkeys.RegistrationFailed += _ =>
                errors.Warn("hotkey.in-use", "A global hotkey couldn't be registered (already in use by another app).");
            _hooked = true;
        }

        RecModeSettings s = settings.Current;
        _nextProfile = Register(s.HotkeyNextProfile, "F8");
        _startStop = Register(s.HotkeyStartStop, "F9");
        _pause = Register(s.HotkeyPauseResume, "F10");
        _screenshot = Register(s.HotkeyScreenshot, "F11");
    }

    /// <summary>Re-registers all hotkeys from the current settings (call after the user remaps one).</summary>
    public void Rebind()
    {
        hotkeys.UnregisterAll();
        Register();
    }

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
    }

    public void Dispose() => hotkeys.Pressed -= OnPressed;
}
