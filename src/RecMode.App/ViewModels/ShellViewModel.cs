using System.ComponentModel;
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
        AboutViewModel about,
        ISettingsService settingsService,
        ThemeManager theme,
        IErrorReporter errors)
    {
        Record = record;
        Library = library;
        Schedule = schedule;
        Settings = settings;
        About = about;
        _settings = settingsService;
        _theme = theme;

        _currentPage = record;
        // Deliberately NOT calling record.OnNavigatedTo() here — see EnsureInitialPageLoaded()'s doc comment.

        NavigateCommand = new RelayCommand<string>(Navigate);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        DismissSnackbarCommand = new RelayCommand(() => SnackbarVisible = false);
        OpenLastRecordingCommand = new RelayCommand(OpenLastRecording, () => LastOutputPath is not null);
        record.PropertyChanged += OnRecordPropertyChanged;
        ExpandCommand = new RelayCommand(() =>
        {
            // "Expand to full window" (compact launcher): always lands on Sidebar — the default full layout
            // — rather than trying to remember whatever non-Compact layout was active before switching in.
            _settings.Current.Layout = ShellLayout.Sidebar;
            _settings.Save();
        });

        _snackbarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _snackbarTimer.Tick += (_, _) => { _snackbarTimer.Stop(); SnackbarVisible = false; };
        errors.ErrorReported += OnErrorReported;
        library.RecordAgainRequested += () => Navigate("Record");

        _layout = settingsService.Current.Layout;
        settingsService.SettingsChanged += (_, _) => Layout = settingsService.Current.Layout; // live layout switch
    }

    private ShellLayout _layout;
    /// <summary>Sidebar vs topbar shell layout (design Layout option); drives which nav the window shows.</summary>
    public ShellLayout Layout { get => _layout; private set => SetProperty(ref _layout, value); }

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
    public AboutViewModel About { get; }

    public ICommand NavigateCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ExpandCommand { get; }
    public IRelayCommand OpenLastRecordingCommand { get; }

    private string? _lastOutputPath;
    /// <summary>Whichever of <see cref="RecordViewModel.LastRecordingPath"/>/<see cref="RecordViewModel.LastScreenshotPath"/>
    /// completed most recently — the title-bar status pill's "jump to Library" target covers both, not just
    /// recordings, since the pill's text (<see cref="RecordViewModel.StatusText"/>) already does.</summary>
    public string? LastOutputPath { get => _lastOutputPath; private set => SetProperty(ref _lastOutputPath, value); }

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

    private bool _initialPageLoaded;

    /// <summary>Record is the shell's default page, but unlike every other page it never goes through
    /// <see cref="Navigate"/> — nothing normally fires its <c>OnNavigatedTo</c> for the initial page. This
    /// constructor used to call it directly, which ran full device discovery
    /// (EnumerateMonitors/GetAvailableEncoders/EnumerateAudioProcesses — see <c>RecordViewModel.LoadDevices</c>)
    /// synchronously before the app's very first window paint, even for a bare <c>--tray</c> launch that shows
    /// no window at all (§3.9: nothing should run before something can see it). Call this instead, once, right
    /// as the hosting window (<c>ShellWindow</c>/<c>CompactWindow</c>) is about to become visible for the
    /// first time — see their <c>IsVisibleChanged</c> handlers. A CLI action that needs devices before any
    /// window is ever shown (e.g. <c>--tray --record</c>) already calls
    /// <see cref="RecordViewModel.EnsureDevicesLoaded"/> directly, independent of this.</summary>
    public void EnsureInitialPageLoaded()
    {
        if (_initialPageLoaded)
        {
            return;
        }

        _initialPageLoaded = true;
        (Record as INavigationAware)?.OnNavigatedTo();
    }

    private void Navigate(string? page)
    {
        object next = page switch
        {
            "Record" => Record,
            "Library" => Library,
            "Schedule" => Schedule,
            "Settings" => Settings,
            "About" => About,
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

    private void OnRecordPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Both LastRecordingPath and LastScreenshotPath get explicitly reset to null at points where there's
        // nothing to jump to yet (e.g. a new recording starting) — only a non-null value should ever move
        // LastOutputPath, so the pill keeps pointing at the last real output instead of going dead early.
        if (e.PropertyName == nameof(RecordViewModel.LastRecordingPath) && Record.LastRecordingPath is not null)
        {
            LastOutputPath = Record.LastRecordingPath;
            OpenLastRecordingCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(RecordViewModel.LastScreenshotPath) && Record.LastScreenshotPath is not null)
        {
            LastOutputPath = Record.LastScreenshotPath;
            OpenLastRecordingCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The title bar's "Saved &lt;filename&gt;" status jumps straight to that recording or
    /// screenshot in the Library — only meaningful once one has actually finished (see
    /// <see cref="LastOutputPath"/>).</summary>
    private void OpenLastRecording()
    {
        if (LastOutputPath is not { } path)
        {
            return;
        }

        Library.RequestSelect(path);
        Navigate("Library");
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
