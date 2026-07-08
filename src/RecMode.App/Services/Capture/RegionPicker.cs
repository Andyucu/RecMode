using System.Windows;
using RecMode.App.Views;
using RecMode.Capture;

namespace RecMode.App.Services;

/// <summary>Default <see cref="IRegionPicker"/> — shows <see cref="RegionSelectWindow"/> modally.</summary>
public sealed class RegionPicker : IRegionPicker
{
    public RegionRect? Pick(MonitorInfo monitor)
    {
        var window = new RegionSelectWindow(monitor);
        if (Application.Current?.MainWindow is { } main && main.IsVisible)
        {
            window.Owner = main;
        }

        return window.ShowDialog() == true ? window.Result : null;
    }
}
