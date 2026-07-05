using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>Edit model for one schedule (shown in the edit dialog). Edits a working copy; <see cref="ApplyTo"/> commits.</summary>
public sealed class ScheduleEditViewModel : ObservableObject
{
    private string _name;
    private ScheduleRecurrence _recurrence;
    private string _time;
    private int _durationMinutes;

    public ScheduleEditViewModel(ScheduleItem source)
    {
        _name = source.Name;
        _recurrence = source.Recurrence;
        _time = source.Time;
        _durationMinutes = source.DurationMinutes;
    }

    public IReadOnlyList<ScheduleRecurrence> Recurrences { get; } =
        [ScheduleRecurrence.Once, ScheduleRecurrence.Daily, ScheduleRecurrence.Weekdays, ScheduleRecurrence.Weekly];
    public IReadOnlyList<int> Durations { get; } = [5, 10, 15, 30, 45, 60, 90, 120, 180];

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public ScheduleRecurrence SelectedRecurrence { get => _recurrence; set => SetProperty(ref _recurrence, value); }
    public string Time { get => _time; set => SetProperty(ref _time, value); }
    public int DurationMinutes { get => _durationMinutes; set => SetProperty(ref _durationMinutes, value); }

    /// <summary>True when the time reads as a valid 24-hour "HH:mm".</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        TimeOnly.TryParseExact(Time.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Commits the edited fields back to <paramref name="target"/>. Call only when <see cref="IsValid"/>.</summary>
    public void ApplyTo(ScheduleItem target)
    {
        target.Name = Name.Trim();
        target.Recurrence = SelectedRecurrence;
        target.Time = Time.Trim();
        target.DurationMinutes = DurationMinutes;
    }
}
