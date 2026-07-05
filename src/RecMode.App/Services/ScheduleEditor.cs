using System.Windows;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>Shows the modal schedule editor. Returns true and mutates <paramref name="item"/> when saved.</summary>
public interface IScheduleEditor
{
    bool Edit(ScheduleItem item);
}

/// <summary>Default <see cref="IScheduleEditor"/> — a modal <see cref="ScheduleEditWindow"/> over a working copy.</summary>
public sealed class ScheduleEditor : IScheduleEditor
{
    public bool Edit(ScheduleItem item)
    {
        var model = new ScheduleEditViewModel(item);
        var window = new ScheduleEditWindow(model);
        if (Application.Current?.MainWindow is { IsVisible: true } main)
        {
            window.Owner = main;
        }

        if (window.ShowDialog() == true)
        {
            model.ApplyTo(item);
            return true;
        }

        return false;
    }
}
