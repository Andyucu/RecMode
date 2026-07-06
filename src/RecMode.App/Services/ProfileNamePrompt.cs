using System.Windows;
using RecMode.App.ViewModels;
using RecMode.App.Views;

namespace RecMode.App.Services;

/// <summary>Shows the modal "Save profile" name prompt. Returns the trimmed name, or null if cancelled.</summary>
public interface IProfileNamePrompt
{
    string? Prompt(string defaultName);
}

/// <summary>Default <see cref="IProfileNamePrompt"/> — a modal <see cref="SaveProfileWindow"/>.</summary>
public sealed class ProfileNamePrompt : IProfileNamePrompt
{
    public string? Prompt(string defaultName)
    {
        var model = new SaveProfileViewModel(defaultName);
        var window = new SaveProfileWindow(model);
        if (Application.Current?.MainWindow is { IsVisible: true } main)
        {
            window.Owner = main;
        }

        return window.ShowDialog() == true ? model.Name.Trim() : null;
    }
}
