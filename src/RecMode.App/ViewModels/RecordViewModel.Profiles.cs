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

    /// <summary>
    /// Library's "Record again": re-applies a past recording's container/frame rate/quality/audio settings
    /// (Library plan §7 feature idea — "one-click record this again"). Deliberately does <b>not</b> touch the
    /// source or encoder: the exact capture target (which monitor/window, or a region rect) was never stored
    /// in <see cref="RecMode.Core.Library.LibraryIndexEntry"/> and generally couldn't be restored reliably even
    /// if it were (a window may no longer exist; a monitor may have reconnected with a different handle) — same
    /// reasoning <see cref="ApplyProfile"/> already uses for saved profiles. Entries recorded before this
    /// feature shipped have <c>Quality</c> default to 0 (skipped) and audio flags default to <c>false</c>
    /// (applied as-is) since neither was captured historically.
    /// </summary>
    public void ApplyRecordAgainSettings(RecMode.Core.Library.LibraryIndexEntry entry)
    {
        if (Enum.TryParse(entry.Container, out MediaContainer container) && Formats.Contains(container))
        {
            SelectedFormat = container;
        }
        if (FrameRates.Contains(entry.Fps))
        {
            SelectedFrameRate = entry.Fps;
        }
        if (entry.Quality > 0)
        {
            Quality = entry.Quality;
        }
        SystemAudioEnabled = entry.SystemAudioEnabled;
        MicEnabled = entry.MicrophoneEnabled;

        // Not a saved profile — show "Custom" rather than implying one of the presets was picked.
        SelectedProfile = _customSentinel;
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
