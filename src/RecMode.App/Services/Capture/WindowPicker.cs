using System.Windows;
using RecMode.App.Views;
using RecMode.Capture;

namespace RecMode.App.Services;

/// <summary>Default <see cref="IWindowPicker"/> — shows <see cref="WindowPickerOverlay"/> modally.</summary>
public sealed class WindowPicker : IWindowPicker
{
    public WindowInfo? Pick()
    {
        var window = new WindowPickerOverlay();
        if (Application.Current?.MainWindow is { } main && main.IsVisible)
        {
            window.Owner = main;
        }

        return window.ShowDialog() == true ? window.Result : null;
    }
}
