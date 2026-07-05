using RecMode.Core.Infrastructure;
using RecMode.Core.Library;
using Xunit;

namespace RecMode.Core.Tests;

public class LibraryIndexTests
{
    // Portable AppPaths in a temp dir → LibraryIndexPath resolves under <dir>\Data\library.json.
    private static AppPaths PortablePaths(TempDir temp)
    {
        File.WriteAllText(Path.Combine(temp.Path, AppPaths.PortableMarkerFileName), "");
        return new AppPaths(temp.Path);
    }

    private static LibraryIndexEntry Entry(string name) =>
        new(name, "Display", "H264", "Mp4", 1920, 1080, 60, 12.5, DateTimeOffset.Now);

    [Fact]
    public void Add_Then_Read_RoundTrips()
    {
        using var temp = new TempDir();
        var index = new LibraryIndex(PortablePaths(temp));
        index.Add(Entry("clip.mp4"));

        LibraryIndexEntry e = index.ByFileName()["clip.mp4"];
        Assert.Equal("Display", e.Source);
        Assert.Equal("H264", e.Codec);
        Assert.Equal(1920, e.Width);
        Assert.Equal(60, e.Fps);
    }

    [Fact]
    public void Add_SameFileName_Replaces()
    {
        using var temp = new TempDir();
        var index = new LibraryIndex(PortablePaths(temp));
        index.Add(Entry("clip.mp4"));
        index.Add(Entry("clip.mp4") with { Codec = "Av1", Width = 2560 });

        IReadOnlyDictionary<string, LibraryIndexEntry> all = index.ByFileName();
        Assert.Single(all);
        Assert.Equal("Av1", all["clip.mp4"].Codec);
        Assert.Equal(2560, all["clip.mp4"].Width);
    }

    [Fact]
    public void PersistsAcrossInstances()
    {
        using var temp = new TempDir();
        AppPaths paths = PortablePaths(temp);
        new LibraryIndex(paths).Add(Entry("a.mp4"));
        new LibraryIndex(paths).Add(Entry("b.mkv"));

        IReadOnlyDictionary<string, LibraryIndexEntry> all = new LibraryIndex(paths).ByFileName();
        Assert.Equal(2, all.Count);
        Assert.Contains("a.mp4", all.Keys);
        Assert.Contains("b.mkv", all.Keys);
    }

    [Fact]
    public void MissingIndex_IsEmpty()
    {
        using var temp = new TempDir();
        Assert.Empty(new LibraryIndex(PortablePaths(temp)).ByFileName());
    }
}
