using RecMode.Core.Infrastructure;
using Xunit;

namespace RecMode.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void PortableMode_KeepsAllStateUnderAppFolder()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, AppPaths.PortableMarkerFileName), "");

        var paths = new AppPaths(temp.Path);

        Assert.True(paths.IsPortable);
        Assert.StartsWith(temp.Path, paths.DataDirectory);
        Assert.StartsWith(temp.Path, paths.RecordingsDirectory);
        Assert.StartsWith(temp.Path, paths.LogsDirectory);
        Assert.Equal(Path.Combine(temp.Path, "Data", AppPaths.SettingsFileName), paths.SettingsFilePath);
    }

    [Fact]
    public void InstalledMode_UsesUserProfileFolders()
    {
        using var temp = new TempDir(); // no marker → installed mode
        var paths = new AppPaths(temp.Path);

        Assert.False(paths.IsPortable);
        Assert.DoesNotContain(temp.Path, paths.DataDirectory);
        Assert.Contains("RecMode", paths.DataDirectory);
    }

    [Fact]
    public void EnsureDirectories_CreatesStateDirs_AndDataIsWritable()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, AppPaths.PortableMarkerFileName), "");

        var paths = new AppPaths(temp.Path);
        paths.EnsureDirectories();

        Assert.True(Directory.Exists(paths.DataDirectory));
        Assert.True(Directory.Exists(paths.CrashDumpDirectory));
        Assert.True(paths.IsDataDirectoryWritable());
    }

    [Fact]
    public void ScreenshotsLiveUnderRecordings()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, AppPaths.PortableMarkerFileName), "");
        var paths = new AppPaths(temp.Path);

        Assert.StartsWith(paths.RecordingsDirectory, paths.ScreenshotsDirectory);
    }
}
