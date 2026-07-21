using CommunityToolkit.Mvvm.ComponentModel;

namespace RecMode.App.ViewModels;

/// <summary>Edit model for the "Save profile" name prompt.</summary>
public sealed class SaveProfileViewModel : ObservableObject
{
    private string _name;

    public SaveProfileViewModel(string defaultName) => _name = defaultName;

    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);
}
