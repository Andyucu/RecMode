using System.Windows;
using RecMode.App.Views;
using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>Default <see cref="IRegionPicker"/> — shows <see cref="RegionSelectWindow"/> modally.</summary>
public sealed class RegionPicker(IOsCapabilities os) : IRegionPicker
{
    public RegionRect? Pick(MonitorInfo monitor, bool excludeFromCapture = false)
    {
        var window = new RegionSelectWindow(monitor, excludeFromCapture ? os : null);
        if (Application.Current?.MainWindow is { } main && main.IsVisible)
        {
            window.Owner = main;
        }

        return window.ShowDialog() == true ? window.Result : null;
    }
}
