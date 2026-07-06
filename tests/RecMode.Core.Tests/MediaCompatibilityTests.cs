using RecMode.Core.Settings;
using Xunit;

namespace RecMode.Core.Tests;

public class MediaCompatibilityTests
{
    [Theory]
    [InlineData(VideoCodec.H264, MediaContainer.Mp4, true)]
    [InlineData(VideoCodec.Hevc, MediaContainer.Mov, true)]
    [InlineData(VideoCodec.Av1, MediaContainer.Mp4, true)]
    [InlineData(VideoCodec.H264, MediaContainer.Mkv, true)]
    [InlineData(VideoCodec.Av1, MediaContainer.WebM, true)]
    [InlineData(VideoCodec.H264, MediaContainer.WebM, false)] // WebM = AV1 only
    [InlineData(VideoCodec.Hevc, MediaContainer.WebM, false)]
    public void IsVideoCompatible(VideoCodec codec, MediaContainer container, bool expected)
    {
        Assert.Equal(expected, MediaCompatibility.IsVideoCompatible(codec, container));
    }

    [Fact]
    public void IncompatibilityReason_EmptyWhenValid_ElseExplains()
    {
        Assert.Equal("", MediaCompatibility.IncompatibilityReason(VideoCodec.Av1, MediaContainer.WebM));
        Assert.Contains("WebM", MediaCompatibility.IncompatibilityReason(VideoCodec.H264, MediaContainer.WebM));
    }
}
