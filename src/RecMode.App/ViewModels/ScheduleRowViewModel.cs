using CommunityToolkit.Mvvm.ComponentModel;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>One row in the Schedule list — wraps a <see cref="ScheduleItem"/> and persists on toggle.</summary>
public sealed class ScheduleRowViewModel : ObservableObject
{
    private static readonly System.Text.CompositeFormat ProfileLabelFormat =
        System.Text.CompositeFormat.Parse(Resources.Strings.Schedule_ProfileLabel);

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

    /// <summary>Source/encoder always follow the current Record settings; frame rate/quality/audio follow
    /// the bound profile, if any (plan §7 backlog — Schedule/Profile binding).</summary>
    public string SourceText => Model.ProfileName is null
        ? Resources.Strings.Schedule_FollowRecordSettings
        : string.Format(null, ProfileLabelFormat, Model.ProfileName);

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

    /// <summary>Refreshes the derived text after the model was edited elsewhere (the edit dialog).</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(WhenText));
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(SourceText));
    }

    private static string RecurrenceLabel(ScheduleRecurrence r) => r switch
    {
        ScheduleRecurrence.Once => "Once",
        ScheduleRecurrence.Daily => "Daily",
        ScheduleRecurrence.Weekdays => "Weekdays",
        ScheduleRecurrence.Weekly => "Weekly",
        _ => r.ToString(),
    };
}
