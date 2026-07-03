using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;
using RecMode.Encoding.Ffmpeg;
using Xunit;

namespace RecMode.Encoding.Tests;

public class FfmpegLocatorTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "recmode-ff-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }

    /// <summary>Minimal in-memory settings service for locator tests.</summary>
    private sealed class FakeSettings(RecModeSettings settings) : ISettingsService
    {
        public RecModeSettings Current { get; } = settings;
        public event EventHandler? SettingsChanged { add { } remove { } }
        public void Load() { }
        public void Save() { }
        public void RequestSave() { }
    }

    private static AppPaths PortablePaths(string root)
    {
        File.WriteAllText(Path.Combine(root, AppPaths.PortableMarkerFileName), "");
        return new AppPaths(root);
    }

    [Fact]
    public void MissingBundledFfmpeg_IsUnavailable_WithBlockingError()
    {
        using var temp = new TempDir();
        var paths = PortablePaths(temp.Path);
        var locator = new FfmpegLocator(paths, new FakeSettings(new RecModeSettings()));

        FfmpegResolution result = locator.Resolve();

        Assert.False(result.IsAvailable);
        Assert.Equal("ffmpeg.bundled-missing", result.Error?.Code);
    }

    [Fact]
    public void PresentBundledFfmpeg_WithoutManifest_IsAvailable_ButUnverified()
    {
        using var temp = new TempDir();
        var paths = PortablePaths(temp.Path);
        Directory.CreateDirectory(paths.FfmpegDirectory);
        File.WriteAllText(Path.Combine(paths.FfmpegDirectory, "ffmpeg.exe"), "not a real binary");
        File.WriteAllText(Path.Combine(paths.FfmpegDirectory, "ffprobe.exe"), "not a real binary");

        var locator = new FfmpegLocator(paths, new FakeSettings(new RecModeSettings()));
        FfmpegResolution result = locator.Resolve();

        Assert.True(result.IsAvailable);
        Assert.False(result.HashVerified);
        Assert.Equal(FfmpegSource.Bundled, result.Source);
        Assert.Equal("ffmpeg.manifest-absent", result.Error?.Code);
    }

    [Fact]
    public void ManifestHashMatch_MarksVerified()
    {
        using var temp = new TempDir();
        var paths = PortablePaths(temp.Path);
        Directory.CreateDirectory(paths.FfmpegDirectory);

        string ffmpegPath = Path.Combine(paths.FfmpegDirectory, "ffmpeg.exe");
        File.WriteAllText(ffmpegPath, "pretend-binary-contents");
        string hash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(ffmpegPath)));

        var manifest = new FfmpegManifest { Build = "test", FfmpegSha256 = hash };
        File.WriteAllText(
            Path.Combine(paths.FfmpegDirectory, FfmpegManifest.FileName),
            System.Text.Json.JsonSerializer.Serialize(manifest));

        var locator = new FfmpegLocator(paths, new FakeSettings(new RecModeSettings()));
        FfmpegResolution result = locator.Resolve();

        Assert.True(result.IsAvailable);
        Assert.True(result.HashVerified);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ManifestHashMismatch_Warns_ButStaysAvailable()
    {
        using var temp = new TempDir();
        var paths = PortablePaths(temp.Path);
        Directory.CreateDirectory(paths.FfmpegDirectory);
        File.WriteAllText(Path.Combine(paths.FfmpegDirectory, "ffmpeg.exe"), "actual-contents");

        var manifest = new FfmpegManifest { Build = "test", FfmpegSha256 = new string('a', 64) };
        File.WriteAllText(
            Path.Combine(paths.FfmpegDirectory, FfmpegManifest.FileName),
            System.Text.Json.JsonSerializer.Serialize(manifest));

        var locator = new FfmpegLocator(paths, new FakeSettings(new RecModeSettings()));
        FfmpegResolution result = locator.Resolve();

        Assert.True(result.IsAvailable);
        Assert.False(result.HashVerified);
        Assert.Equal("ffmpeg.hash-mismatch", result.Error?.Code);
    }

    [Fact]
    public void UserOverride_MissingFile_IsBlocking()
    {
        using var temp = new TempDir();
        var paths = PortablePaths(temp.Path);
        var settings = new RecModeSettings { FfmpegPathOverride = Path.Combine(temp.Path, "nope", "ffmpeg.exe") };

        var locator = new FfmpegLocator(paths, new FakeSettings(settings));
        FfmpegResolution result = locator.Resolve();

        Assert.False(result.IsAvailable);
        Assert.Equal("ffmpeg.override-missing", result.Error?.Code);
        Assert.Equal(FfmpegSource.UserOverride, result.Source);
    }
}
