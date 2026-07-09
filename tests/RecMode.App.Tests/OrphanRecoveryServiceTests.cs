using RecMode.App.Services;
using Xunit;

namespace RecMode.App.Tests;

/// <summary>Exercises <see cref="OrphanRecoveryService"/>'s pure path logic against a real scratch directory
/// (no mocking needed — the only dependency is <c>File.Exists</c>).</summary>
public class OrphanRecoveryServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("recmode-orphan-tests-").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void UniqueMp4Path_StripsOrphanSuffix()
    {
        string orphan = Path.Combine(_dir, "clip.recording.mkv");
        string result = OrphanRecoveryService.UniqueMp4Path(orphan);
        Assert.Equal(Path.Combine(_dir, "clip.mp4"), result);
    }

    [Fact]
    public void UniqueMp4Path_AvoidsClobberingAnExistingFile()
    {
        string orphan = Path.Combine(_dir, "clip.recording.mkv");
        File.WriteAllText(Path.Combine(_dir, "clip.mp4"), "existing");

        string result = OrphanRecoveryService.UniqueMp4Path(orphan);

        Assert.Equal(Path.Combine(_dir, "clip (recovered 1).mp4"), result);
    }

    [Fact]
    public void UniqueMp4Path_IncrementsPastMultipleExistingRecoveries()
    {
        string orphan = Path.Combine(_dir, "clip.recording.mkv");
        File.WriteAllText(Path.Combine(_dir, "clip.mp4"), "");
        File.WriteAllText(Path.Combine(_dir, "clip (recovered 1).mp4"), "");
        File.WriteAllText(Path.Combine(_dir, "clip (recovered 2).mp4"), "");

        string result = OrphanRecoveryService.UniqueMp4Path(orphan);

        Assert.Equal(Path.Combine(_dir, "clip (recovered 3).mp4"), result);
    }
}
