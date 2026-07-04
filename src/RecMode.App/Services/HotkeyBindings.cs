using RecMode.App.ViewModels;
using RecMode.Core.Errors;

namespace RecMode.App.Services;

/// <summary>
/// Binds the default global hotkeys (F9 start/stop, F10 pause/resume, F11 screenshot) to the Record view
/// model (plan Phase 5). Remappable hotkeys UI is Phase 9; keys are fixed for now.
/// </summary>
public sealed class HotkeyBindings(GlobalHotkeys hotkeys, RecordViewModel record, IErrorReporter errors) : IDisposable
{
    private int _startStop = -1;
    private int _pause = -1;
    private int _screenshot = -1;

    public void Register()
    {
        hotkeys.Pressed += OnPressed;
        hotkeys.RegistrationFailed += vk =>
            errors.Warn("hotkey.in-use", "A global hotkey couldn't be registered (already in use by another app).");

        _startStop = hotkeys.Register(0, VirtualKeys.F9);
        _pause = hotkeys.Register(0, VirtualKeys.F10);
        _screenshot = hotkeys.Register(0, VirtualKeys.F11);
    }

    private void OnPressed(int id)
    {
        if (id == _startStop)
        {
            if (record.RecordCommand.CanExecute(null)) record.RecordCommand.Execute(null);
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
