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
    private DayOfWeek _selectedWeeklyDay;
    private string _selectedProfileOption;
    private DateTime _onceDate;
    private readonly DateTimeOffset? _originalOnceAt;

    public ScheduleEditViewModel(ScheduleItem source, IReadOnlyList<string> profileNames)
    {
        _name = source.Name;
        _recurrence = source.Recurrence;
        _time = source.Time;
        _durationMinutes = source.DurationMinutes;
        _onceDate = (source.OnceAt ?? DateTimeOffset.Now.AddMinutes(30)).LocalDateTime.Date;
        _originalOnceAt = source.OnceAt;
        _selectedWeeklyDay = source.WeeklyDay ?? source.LastFiredUtc?.ToLocalTime().DayOfWeek ?? DateTime.Today.DayOfWeek;

        ProfileOptions = [FollowRecordSettingsOption, .. profileNames];
        _selectedProfileOption = source.ProfileName is { } saved && profileNames.Contains(saved)
            ? saved
            : FollowRecordSettingsOption;
    }

    /// <summary>Sentinel meaning "no profile bound — this schedule uses whatever the Record screen is
    /// currently set to when it fires" (the original behavior, and still the default for new schedules).</summary>
    public static string FollowRecordSettingsOption => Resources.Strings.Schedule_FollowRecordSettings;

    public IReadOnlyList<ScheduleRecurrence> Recurrences { get; } =
        [ScheduleRecurrence.Once, ScheduleRecurrence.Daily, ScheduleRecurrence.Weekdays, ScheduleRecurrence.Weekly];
    public IReadOnlyList<int> Durations { get; } = [5, 10, 15, 30, 45, 60, 90, 120, 180];
    public IReadOnlyList<DayOfWeek> Weekdays { get; } = Enum.GetValues<DayOfWeek>();

    /// <summary><see cref="FollowRecordSettingsOption"/> first, then every built-in + custom Recording Profile
    /// by name (plan §7 backlog — Schedule/Profile binding).</summary>
    public IReadOnlyList<string> ProfileOptions { get; }

    public string Name { get => _name; set { if (SetProperty(ref _name, value)) { OnPropertyChanged(nameof(ErrorText)); OnPropertyChanged(nameof(IsValid)); } } }
    public ScheduleRecurrence SelectedRecurrence { get => _recurrence; set => SetProperty(ref _recurrence, value); }
    public string Time { get => _time; set { if (SetProperty(ref _time, value)) { OnPropertyChanged(nameof(ErrorText)); OnPropertyChanged(nameof(IsValid)); } } }
    public int DurationMinutes { get => _durationMinutes; set => SetProperty(ref _durationMinutes, value); }
    public DayOfWeek SelectedWeeklyDay { get => _selectedWeeklyDay; set => SetProperty(ref _selectedWeeklyDay, value); }
    public string SelectedProfileOption { get => _selectedProfileOption; set => SetProperty(ref _selectedProfileOption, value); }
    public DateTime OnceDate { get => _onceDate; set => SetProperty(ref _onceDate, value); }

    /// <summary>True when the time reads as a valid 24-hour "HH:mm".</summary>
    public string ErrorText => string.IsNullOrWhiteSpace(Name)
        ? Resources.Strings.ScheduleEdit_NameRequired
        : !TryParseTime(Time, out _) ? Resources.Strings.ScheduleEdit_InvalidTime : string.Empty;
    public bool IsValid => string.IsNullOrEmpty(ErrorText);

    private static bool TryParseTime(string value, out TimeOnly time)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        // Accept the user's configured short-time pattern (for example, 09:30 or 9:30 AM) and the
        // persisted invariant form. Exact parsing keeps malformed-but-parseable values such as 9:00
        // from slipping through a 24-hour HH:mm field on cultures whose short pattern is HH:mm.
        string culturePattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
        return TimeOnly.TryParseExact(trimmed, culturePattern, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out time) ||
            TimeOnly.TryParseExact(trimmed, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>Commits the edited fields back to <paramref name="target"/>. Call only when <see cref="IsValid"/>.</summary>
    public void ApplyTo(ScheduleItem target)
    {
        target.Name = Name.Trim();
        target.Recurrence = SelectedRecurrence;
        if (TryParseTime(Time, out var parsedTime)) target.Time = parsedTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        target.DurationMinutes = DurationMinutes;
        target.WeeklyDay = SelectedRecurrence == ScheduleRecurrence.Weekly ? SelectedWeeklyDay : null;
        target.ProfileName = SelectedProfileOption == FollowRecordSettingsOption ? null : SelectedProfileOption;
        if (SelectedRecurrence == ScheduleRecurrence.Once &&
            TryParseTime(target.Time, out TimeOnly time))
        {
            target.OnceAt = new DateTimeOffset(OnceDate.Date.Add(time.ToTimeSpan()), TimeZoneInfo.Local.GetUtcOffset(OnceDate.Date.Add(time.ToTimeSpan())));
        }
        else
        {
            target.OnceAt = null;
        }

        // ScheduleEvaluator.IsOnceDue treats any non-null LastFiredUtc as "this Once schedule already ran,
        // never again" — full stop, regardless of OnceAt. Without this, editing a fired Once schedule to a
        // new date and re-enabling it looked like it worked (the row showed "On", the new date showed in the
        // editor) but the schedule silently never fired again. Only reset when the target date/time actually
        // moved — a plain rename or profile-only edit shouldn't resurrect a schedule that genuinely already
        // ran at its original, unchanged time.
        if (SelectedRecurrence == ScheduleRecurrence.Once && target.OnceAt != _originalOnceAt)
        {
            target.LastFiredUtc = null;
            target.LastFiredOccurrence = null;
        }
    }
}
