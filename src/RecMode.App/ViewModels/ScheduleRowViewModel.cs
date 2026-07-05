using CommunityToolkit.Mvvm.ComponentModel;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>One row in the Schedule list — wraps a <see cref="ScheduleItem"/> and persists on toggle.</summary>
public sealed class ScheduleRowViewModel : ObservableObject
{
    private readonly Action _persist;

    public ScheduleRowViewModel(ScheduleItem model, Action persist)
    {
        Model = model;
        _persist = persist;
    }

    public ScheduleItem Model { get; }

    public string Name => Model.Name;

    /// <summary>e.g. "Weekdays · 09:00 · 15 min".</summary>
    public string WhenText => $"{RecurrenceLabel(Model.Recurrence)} · {Model.Time} · {Model.DurationMinutes} min";

    /// <summary>Source/encoder follow the current Record settings for the MVP (per the design subtext).</summary>
    public string SourceText => "Follows Record settings";

    public bool Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled != value)
            {
                Model.Enabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StateLabel));
                _persist();
            }
        }
    }

    public string StateLabel => Model.Enabled ? "On" : "Off";

    private static string RecurrenceLabel(ScheduleRecurrence r) => r switch
    {
        ScheduleRecurrence.Once => "Once",
        ScheduleRecurrence.Daily => "Daily",
        ScheduleRecurrence.Weekdays => "Weekdays",
        ScheduleRecurrence.Weekly => "Weekly",
        _ => r.ToString(),
    };
}
