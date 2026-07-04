using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.App.Themes;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>
/// Hosts the sidebar navigation and the current page (plan: sidebar-only shell in Phase 1). Owns the
/// theme toggle. Pages are injected and swapped through <see cref="CurrentPage"/>; a <see cref="INavigationAware"/>
/// page is notified so it can honour the §3.9 lifecycle.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _theme;

    private object _currentPage;
    private string _selectedNav = "Record";

    public ShellViewModel(
        RecordViewModel record,
        LibraryViewModel library,
        ScheduleViewModel schedule,
        SettingsViewModel settings,
        ISettingsService settingsService,
        ThemeManager theme)
    {
        Record = record;
        Library = library;
        Schedule = schedule;
        Settings = settings;
        _settings = settingsService;
        _theme = theme;

        _currentPage = record;
        (record as INavigationAware)?.OnNavigatedTo();

        NavigateCommand = new RelayCommand<string>(Navigate);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
    }

    public RecordViewModel Record { get; }
    public LibraryViewModel Library { get; }
    public ScheduleViewModel Schedule { get; }
    public SettingsViewModel Settings { get; }

    public ICommand NavigateCommand { get; }
    public ICommand ToggleThemeCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string SelectedNav
    {
        get => _selectedNav;
        set => SetProperty(ref _selectedNav, value);
    }

    public bool IsDark => _theme.IsDark;

    private void Navigate(string? page)
    {
        object next = page switch
        {
            "Record" => Record,
            "Library" => Library,
            "Schedule" => Schedule,
            "Settings" => Settings,
            _ => Record,
        };

        if (ReferenceEquals(next, CurrentPage))
        {
            return;
        }

        (CurrentPage as INavigationAware)?.OnNavigatedFrom();
        CurrentPage = next;
        SelectedNav = page ?? "Record";
        (next as INavigationAware)?.OnNavigatedTo();
    }

    private void ToggleTheme()
    {
        var next = _theme.IsDark ? AppTheme.Light : AppTheme.Dark;
        _settings.Current.Theme = next;
        _theme.ApplyTheme(next);
        _theme.ApplyAccent(_settings.Current.Accent);
        _settings.RequestSave();
        OnPropertyChanged(nameof(IsDark));
    }
}
