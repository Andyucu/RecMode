using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.App.Services;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>
/// Scheduled-recordings screen (plan Phase 6 — design-faithful UI + persisted data model). Schedules are
/// stored in settings and survive restarts; the engine that actually fires them is Phase 8. Loads on
/// navigation (§3.9). New schedules open the editor; toggling/editing/deleting persists immediately.
/// </summary>
public sealed class ScheduleViewModel : ObservableObject, INavigationAware
{
    private readonly ISettingsService _settings;
    private readonly IScheduleEditor _editor;

    public ScheduleViewModel(ISettingsService settings, IScheduleEditor editor)
    {
        _settings = settings;
        _editor = editor;
        NewScheduleCommand = new RelayCommand(NewSchedule);
        EditCommand = new RelayCommand<ScheduleRowViewModel>(Edit);
        DeleteCommand = new RelayCommand<ScheduleRowViewModel>(Delete);
    }

    public ObservableCollection<ScheduleRowViewModel> Schedules { get; } = [];

    public IRelayCommand NewScheduleCommand { get; }
    public IRelayCommand<ScheduleRowViewModel> EditCommand { get; }
    public IRelayCommand<ScheduleRowViewModel> DeleteCommand { get; }

    public bool IsEmpty => Schedules.Count == 0;

    public void OnNavigatedTo() => Load();

    public void OnNavigatedFrom() => Schedules.Clear();

    private void Load()
    {
        Schedules.Clear();
        foreach (ScheduleItem item in _settings.Current.Schedules)
        {
            Schedules.Add(new ScheduleRowViewModel(item, _settings.RequestSave));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void NewSchedule()
    {
        var item = new ScheduleItem
        {
            Name = "New schedule",
            Recurrence = ScheduleRecurrence.Once,
            Time = DateTime.Now.AddMinutes(30).ToString("HH:mm"),
            DurationMinutes = 30,
            Enabled = true,
        };

        // Open the editor immediately so the user configures the new schedule; keep the default if they cancel.
        _editor.Edit(item);

        _settings.Current.Schedules.Add(item);
        Schedules.Add(new ScheduleRowViewModel(item, _settings.RequestSave));
        _settings.RequestSave();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Edit(ScheduleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (_editor.Edit(row.Model))
        {
            row.RefreshDisplay();
            _settings.RequestSave();
        }
    }

    private void Delete(ScheduleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _settings.Current.Schedules.RemoveAll(x => x.Id == row.Model.Id);
        Schedules.Remove(row);
        _settings.RequestSave();
        OnPropertyChanged(nameof(IsEmpty));
    }
}
