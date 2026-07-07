using System.Collections.ObjectModel;
using System.Windows;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    private readonly RecordingProfile _customSentinel = new() { Name = Resources.Strings.Profile_Custom, IsBuiltIn = true };
    private RecordingProfile? _selectedProfile;
    private bool _loadingProfiles;

    public ObservableCollection<RecordingProfile> Profiles { get; } = [];

    /// <summary>
    /// Recording profiles (plan §7 backlog #4, pulled forward): built-in presets (Tutorial/Gameplay/Meeting/
    /// Bug report/GIF clip/High-quality archive) plus any user-saved ones, plus a "Custom" sentinel meaning
    /// "no preset — settings below are edited directly". Picking one applies container/frame rate/quality/audio;
    /// it doesn't touch the source or the encoder (hw availability is machine-specific).
    /// </summary>
    public RecordingProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value))
            {
                return;
            }

            // Profiles.Clear() (in LoadProfiles, e.g. after Save/Delete) momentarily nulls the ComboBox's
            // SelectedItem; that flows back through this TwoWay-bound setter and would otherwise clobber the
            // selection we're about to restore. Ignore side effects while a profile-list refresh is in flight.
            if (_loadingProfiles)
            {
                return;
            }

            _settings.Current.SelectedProfileName = ReferenceEquals(value, _customSentinel) ? null : value?.Name;
            _settings.RequestSave();
            OnPropertyChanged(nameof(CanDeleteProfile));
            DeleteProfileCommand.NotifyCanExecuteChanged();
            if (value is not null && !ReferenceEquals(value, _customSentinel))
            {
                ApplyProfile(value);
            }
        }
    }

    public bool CanDeleteProfile => SelectedProfile is { IsBuiltIn: false };

    private void LoadProfiles()
    {
        _loadingProfiles = true;
        try
        {
            Profiles.Clear();
            Profiles.Add(_customSentinel);
            foreach (RecordingProfile p in RecordingProfiles.BuiltIn)
            {
                Profiles.Add(p);
            }
            foreach (RecordingProfile p in _settings.Current.CustomProfiles)
            {
                Profiles.Add(p);
            }

            string? savedName = _settings.Current.SelectedProfileName;
            _selectedProfile = savedName is null ? _customSentinel : Profiles.FirstOrDefault(p => p.Name == savedName) ?? _customSentinel;
            OnPropertyChanged(nameof(SelectedProfile));
        }
        finally
        {
            _loadingProfiles = false;
        }

        OnPropertyChanged(nameof(CanDeleteProfile));
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    private void ApplyProfile(RecordingProfile profile)
    {
        SelectedFormat = profile.Container;
        if (FrameRates.Contains(profile.FrameRate))
        {
            SelectedFrameRate = profile.FrameRate;
        }
        Quality = profile.Quality;
        SystemAudioEnabled = profile.SystemAudioEnabled;
        MicEnabled = profile.MicrophoneEnabled;
        _settings.Current.AudioCodec = profile.AudioCodec;
        _settings.Current.AudioBitrateKbps = profile.AudioBitrateKbps;
        _settings.RequestSave();
    }

    private void SaveProfile()
    {
        string defaultName = SelectedProfile is { IsBuiltIn: false } current ? current.Name : "My profile";
        string? name = _profilePrompt.Prompt(defaultName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (RecordingProfiles.BuiltIn.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(Resources.Strings.Profile_NameTaken, Resources.Strings.Profile_SaveTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profile = new RecordingProfile
        {
            Name = name,
            Container = SelectedFormat,
            FrameRate = SelectedFrameRate,
            Quality = Quality,
            SystemAudioEnabled = SystemAudioEnabled,
            MicrophoneEnabled = MicEnabled,
            AudioCodec = _settings.Current.AudioCodec,
            AudioBitrateKbps = _settings.Current.AudioBitrateKbps,
            IsBuiltIn = false,
        };

        _settings.Current.CustomProfiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _settings.Current.CustomProfiles.Add(profile);
        _settings.Current.SelectedProfileName = name;
        _settings.Save();

        LoadProfiles();
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is not { IsBuiltIn: false } profile)
        {
            return;
        }

        _settings.Current.CustomProfiles.RemoveAll(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        _settings.Current.SelectedProfileName = null;
        _settings.Save();

        LoadProfiles();
    }
}
