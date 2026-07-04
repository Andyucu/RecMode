using CommunityToolkit.Mvvm.ComponentModel;

namespace RecMode.App.ViewModels;

/// <summary>Placeholder Schedule (plan Phase 1: stub; scheduler arrives in Phase 8).</summary>
public sealed class ScheduleViewModel : ObservableObject
{
    public string Message => RecMode.App.Resources.Strings.Schedule_Empty;
}
