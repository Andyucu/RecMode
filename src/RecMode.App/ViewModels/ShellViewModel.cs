using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecMode.App.Themes;
using RecMode.Core.Errors;
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

    private readonly DispatcherTimer _snackbarTimer;
    private string _snackbarMessage = "";
    private bool _snackbarVisible;
    private bool _snackbarIsError;

    public ShellViewModel(
        RecordViewModel record,
        LibraryViewModel library,
        ScheduleViewModel schedule,
        SettingsViewModel settings,
        ISettingsService settingsService,
        ThemeManager theme,
        IErrorReporter errors)
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
        DismissSnackbarCommand = new RelayCommand(() => SnackbarVisible = false);

        _snackbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _snackbarTimer.Tick += (_, _) => { _snackbarTimer.Stop(); SnackbarVisible = false; };
        errors.ErrorReported += OnErrorReported;
    }

    public ICommand DismissSnackbarCommand { get; }

    public string SnackbarMessage { get => _snackbarMessage; private set => SetProperty(ref _snackbarMessage, value); }
    public bool SnackbarVisible { get => _snackbarVisible; private set => SetProperty(ref _snackbarVisible, value); }
    public bool SnackbarIsError { get => _snackbarIsError; private set => SetProperty(ref _snackbarIsError, value); }

    private void OnErrorReported(object? sender, RecModeError error)
    {
        void Show()
        {
            SnackbarMessage = error.Suggestion is null ? error.Message : $"{error.Message} {error.Suggestion}";
            SnackbarIsError = error.Severity is ErrorSeverity.BlockingError or ErrorSeverity.FatalFinalizationError;
            SnackbarVisible = true;

            // Warnings auto-dismiss; blocking/fatal stay until dismissed.
            _snackbarTimer.Stop();
            if (!SnackbarIsError)
            {
                _snackbarTimer.Start();
            }
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            Show();
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(Show);
        }
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
