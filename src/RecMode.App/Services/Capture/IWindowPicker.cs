using RecMode.Capture;

namespace RecMode.App.Services;

/// <summary>Shows the "pick a window with the mouse" overlay and returns the chosen window, or null if cancelled.</summary>
public interface IWindowPicker
{
    WindowInfo? Pick();
}
