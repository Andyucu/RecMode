using RecMode.Core.Settings;
using Xunit;

namespace RecMode.Core.Tests;

public class PerformanceBoundsTests
{
    private static readonly int[] Sixteen = [0, 2, 4, 6, 8, 12, 16];
    private static readonly int[] Four = [0, 2, 4];
    private static readonly int[] One = [0];
    private static readonly int[] CoreCounts = [2, 6, 10, 24];

    [Fact]
    public void SixteenThreads_MatchesTheClassicList()
    {
        Assert.Equal(Sixteen, PerformanceBounds.ThreadCapOptions(16));
    }

    [Fact]
    public void FourCores_NeverOffersMoreThanFour()
    {
        int[] opts = [.. PerformanceBounds.ThreadCapOptions(4)];
        Assert.Equal(Four, opts);
        Assert.DoesNotContain(opts, o => o > 4);
    }

    [Fact]
    public void AlwaysIncludesAutoAndTheCoreCount()
    {
        foreach (int cores in CoreCounts)
        {
            IReadOnlyList<int> opts = PerformanceBounds.ThreadCapOptions(cores);
            Assert.Equal(0, opts[0]);       // Auto first
            Assert.Contains(cores, opts);   // the exact core count is always selectable
        }
    }

    [Fact]
    public void SingleCore_IsJustAuto()
    {
        Assert.Equal(One, PerformanceBounds.ThreadCapOptions(1));
    }
}
