using System.Windows;
using RecMode.App.Views;
using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>Shows the pre-roll countdown before recording. Returns false if the user cancelled (Esc).</summary>
public interface ICountdownController
{
    /// <summary>Runs an <paramref name="seconds"/>-second countdown over <paramref name="monitor"/>. True = proceed, false = cancelled.</summary>
    bool Run(MonitorInfo monitor, int seconds);
}

/// <summary>Default <see cref="ICountdownController"/> — a modal <see cref="CountdownWindow"/> on the target display.</summary>
public sealed class CountdownController(IOsCapabilities os) : ICountdownController
{
    public bool Run(MonitorInfo monitor, int seconds)
    {
        if (seconds <= 0)
        {
            return true;
        }

        var window = new CountdownWindow(monitor, seconds, os);
        if (Application.Current?.MainWindow is { IsVisible: true } main)
        {
            window.Owner = main;
        }

        return window.ShowDialog() == true;
    }
}
