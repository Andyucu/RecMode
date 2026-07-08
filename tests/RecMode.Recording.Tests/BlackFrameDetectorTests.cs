using RecMode.App.Services;
using Xunit;

namespace RecMode.Recording.Tests;

public class BlackFrameDetectorTests
{
    [Fact]
    public void IsLikelyBlack_TrueForAllZeroLuma()
    {
        byte[] frame = new byte[1920 * 1080];
        Assert.True(BlackFrameDetector.IsLikelyBlack(frame, frame.Length));
    }

    [Fact]
    public void IsLikelyBlack_FalseForNormalContent()
    {
        byte[] frame = new byte[1920 * 1080];
        Array.Fill(frame, (byte)128);
        Assert.False(BlackFrameDetector.IsLikelyBlack(frame, frame.Length));
    }

    [Fact]
    public void IsLikelyBlack_FalseWhenOnlyASampledPixelIsBright()
    {
        // A single bright pixel that happens to land on a sampled stride index must flip the result to false.
        byte[] frame = new byte[1920 * 1080];
        int stepSize = Math.Max(1, frame.Length / 256);
        frame[stepSize * 10] = 255;

        Assert.False(BlackFrameDetector.IsLikelyBlack(frame, frame.Length));
    }

    [Fact]
    public void IsLikelyBlack_FalseForZeroLength()
    {
        Assert.False(BlackFrameDetector.IsLikelyBlack([], 0));
    }

    [Fact]
    public void IsLikelyBlack_TrueForStudioBlackJustUnderThreshold()
    {
        byte[] frame = new byte[1920 * 1080];
        Array.Fill(frame, (byte)19);
        Assert.True(BlackFrameDetector.IsLikelyBlack(frame, frame.Length));
    }

    [Fact]
    public void IsLikelyBlack_FalseAtThreshold()
    {
        byte[] frame = new byte[1920 * 1080];
        Array.Fill(frame, (byte)20);
        Assert.False(BlackFrameDetector.IsLikelyBlack(frame, frame.Length));
    }
}
