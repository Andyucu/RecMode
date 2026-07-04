using CommunityToolkit.Mvvm.ComponentModel;

namespace RecMode.App.ViewModels;

/// <summary>Placeholder Library (plan Phase 1: stub; real library index arrives in Phase 5).</summary>
public sealed class LibraryViewModel : ObservableObject
{
    public string Message => RecMode.App.Resources.Strings.Library_Empty;
}
