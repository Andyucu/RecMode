using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RecMode.App.Themes;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

/// <summary>Basic Settings for Phase 1: theme, accent, and output folder (plan: "theme + output folder only").</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _theme;
    private readonly IAppPaths _paths;

    private AppTheme _selectedTheme;
    private AccentColor _selectedAccent;
    private string _outputFolder;

    public SettingsViewModel(ISettingsService settings, ThemeManager theme, IAppPaths paths)
    {
        _settings = settings;
        _theme = theme;
        _paths = paths;

        _selectedTheme = settings.Current.Theme;
        _selectedAccent = settings.Current.Accent;
        _outputFolder = settings.Current.OutputFolder ?? paths.RecordingsDirectory;

        BrowseCommand = new RelayCommand(BrowseFolder);
    }

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    public IReadOnlyList<AccentColor> Accents { get; } =
        [AccentColor.Blue, AccentColor.Red, AccentColor.Purple, AccentColor.Teal, AccentColor.Orange];

    public ICommand BrowseCommand { get; }

    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _settings.Current.Theme = value;
                _theme.ApplyTheme(value);
                _theme.ApplyAccent(_settings.Current.Accent);
                _settings.RequestSave();
            }
        }
    }

    public AccentColor SelectedAccent
    {
        get => _selectedAccent;
        set
        {
            if (SetProperty(ref _selectedAccent, value))
            {
                _settings.Current.Accent = value;
                _theme.ApplyAccent(value);
                _settings.RequestSave();
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetProperty(ref _outputFolder, value))
            {
                _settings.Current.OutputFolder = value;
                _settings.RequestSave();
            }
        }
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose output folder",
            InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : _paths.RecordingsDirectory,
        };
        if (dialog.ShowDialog() == true)
        {
            OutputFolder = dialog.FolderName;
        }
    }
}
