using RecMode.Core.Recording;
using Xunit;

namespace RecMode.Core.Tests;

public class FilenameBuilderTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 4, 14, 22, 5, TimeSpan.Zero);

    [Fact]
    public void ExpandsDateTimeTokens()
    {
        string name = FilenameBuilder.BuildFileName("RecMode {date} {time}", When, "Display", "H264", "mp4");
        Assert.Equal("RecMode 2026-07-04 14-22-05.mp4", name);
    }

    [Fact]
    public void ExpandsSourceAndCodecTokens()
    {
        string name = FilenameBuilder.BuildFileName("{source} {codec}", When, "Display1", "Av1", "mkv");
        Assert.Equal("Display1 Av1.mkv", name);
    }

    [Fact]
    public void SanitizesIllegalCharacters()
    {
        string name = FilenameBuilder.BuildFileName("a/b:c*d", When, "s", "c", "mp4");
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('*', name);
    }

    [Fact]
    public void EmptyPatternFallsBackToDefault()
    {
        string name = FilenameBuilder.BuildFileName("", When, "Display", "H264", "mp4");
        Assert.StartsWith("RecMode ", name);
    }

    [Fact]
    public void BuildUniquePath_SuffixesOnCollision()
    {
        using var temp = new TempDir();
        string first = FilenameBuilder.BuildUniquePath(temp.Path, "clip.mp4");
        File.WriteAllText(first, "x");
        string second = FilenameBuilder.BuildUniquePath(temp.Path, "clip.mp4");

        Assert.EndsWith("clip.mp4", first);
        Assert.EndsWith("clip (2).mp4", second);
    }
}
