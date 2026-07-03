using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using Xunit;

namespace RecMode.Core.Tests;

public class SettingsServiceTests
{
    private static (SettingsService Service, RecordingErrorReporter Errors, AppPaths Paths) NewService(TempDir temp)
    {
        File.WriteAllText(Path.Combine(temp.Path, AppPaths.PortableMarkerFileName), "");
        var paths = new AppPaths(temp.Path);
        paths.EnsureDirectories();
        var errors = new RecordingErrorReporter();
        return (new SettingsService(paths, errors), errors, paths);
    }

    [Fact]
    public void Load_WithNoFile_YieldsDefaults()
    {
        using var temp = new TempDir();
        var (service, _, _) = NewService(temp);

        service.Load();

        Assert.Equal(RecModeSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.Equal(AppTheme.System, service.Current.Theme);
        Assert.True(service.Current.SafeRecording);
        Assert.Equal(60, service.Current.FrameRate);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        using var temp = new TempDir();
        var (service, _, paths) = NewService(temp);

        service.Load();
        service.Current.Theme = AppTheme.Dark;
        service.Current.Accent = AccentColor.Purple;
        service.Current.Quality = 42;
        service.Current.Codec = VideoCodec.Av1;
        service.Save();

        Assert.True(File.Exists(paths.SettingsFilePath));

        var reloaded = new SettingsService(paths, new RecordingErrorReporter());
        reloaded.Load();

        Assert.Equal(AppTheme.Dark, reloaded.Current.Theme);
        Assert.Equal(AccentColor.Purple, reloaded.Current.Accent);
        Assert.Equal(42, reloaded.Current.Quality);
        Assert.Equal(VideoCodec.Av1, reloaded.Current.Codec);
    }

    [Fact]
    public void EnumsPersistAsStrings()
    {
        using var temp = new TempDir();
        var (service, _, paths) = NewService(temp);
        service.Load();
        service.Current.Theme = AppTheme.Dark;
        service.Save();

        string json = File.ReadAllText(paths.SettingsFilePath);
        Assert.Contains("\"Dark\"", json);
        Assert.DoesNotContain("\"Theme\": 2", json);
    }

    [Fact]
    public void CorruptFile_RecoversToDefaults_AndReportsWarning_AndBacksUp()
    {
        using var temp = new TempDir();
        var (service, errors, paths) = NewService(temp);
        File.WriteAllText(paths.SettingsFilePath, "{ this is not valid json ");

        service.Load();

        Assert.Equal(RecModeSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.Contains(errors.Errors, e => e.Code == "settings.corrupt");
        Assert.True(File.Exists(paths.SettingsFilePath + ".corrupt"));
    }

    [Fact]
    public void OlderSchemaVersion_IsMigratedToCurrent()
    {
        using var temp = new TempDir();
        var (service, _, paths) = NewService(temp);
        // A pre-versioning document (SchemaVersion 0) with a couple of known fields.
        File.WriteAllText(paths.SettingsFilePath, """{ "SchemaVersion": 0, "Theme": "Light" }""");

        service.Load();

        Assert.Equal(RecModeSettings.CurrentSchemaVersion, service.Current.SchemaVersion);
        Assert.Equal(AppTheme.Light, service.Current.Theme);
    }
}
