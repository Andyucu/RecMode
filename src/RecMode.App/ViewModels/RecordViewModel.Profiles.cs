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

    public void CycleRecordingProfile()
    {
        if (IsRecording)
        {
            return;
        }

        RecordingProfile[] candidates = [.. Profiles.Where(p => !ReferenceEquals(p, _customSentinel))];
        if (candidates.Length == 0)
        {
            return;
        }

        int current = Array.FindIndex(candidates, p => ReferenceEquals(p, SelectedProfile) || p.Name == SelectedProfile?.Name);
        SelectedProfile = candidates[(current + 1) % candidates.Length];
    }

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

    /// <summary>Internal (rather than private) so <see cref="SchedulerService"/> can apply a schedule's bound
    /// profile before firing, without going through <see cref="SelectedProfile"/> (which would also persist
    /// that profile as the user's manually-selected one — not appropriate for an unattended, timer-fired
    /// recording that shouldn't silently change what the Record screen shows next time it's opened).</summary>
    internal void ApplyProfile(RecordingProfile profile)
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

    /// <summary>Applies a schedule-bound profile only for the synchronous recording startup window. The
    /// coordinator captures its options during Start; afterwards the user's visible/persisted defaults are
    /// restored, so an unattended schedule never silently changes them.</summary>
    internal IDisposable ApplyProfileForSchedule(RecordingProfile profile)
    {
        var previous = new ScheduledProfileState(
            SelectedFormat, SelectedFrameRate, Quality, SystemAudioEnabled, MicEnabled,
            _settings.Current.AudioCodec, _settings.Current.AudioBitrateKbps);
        ApplyProfile(profile);
        return new Restore(() =>
        {
            SelectedFormat = previous.Format;
            SelectedFrameRate = previous.FrameRate;
            Quality = previous.Quality;
            SystemAudioEnabled = previous.SystemAudio;
            MicEnabled = previous.Microphone;
            _settings.Current.AudioCodec = previous.AudioCodec;
            _settings.Current.AudioBitrateKbps = previous.AudioBitrate;
            _settings.RequestSave();
        });
    }

    private sealed record ScheduledProfileState(MediaContainer Format, int FrameRate, int Quality,
        bool SystemAudio, bool Microphone, AudioCodec AudioCodec, int AudioBitrate);

    private sealed class Restore(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
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
