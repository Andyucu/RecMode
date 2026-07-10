using RecMode.App.Services;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using Xunit;

namespace RecMode.Recording.Tests;

public class EncoderFallbackChainTests
{
    private static EncoderInfo Info(string id, VideoCodec codec, EncoderBackend backend) => new()
    {
        FfmpegId = id,
        DisplayName = id,
        Codec = codec,
        Backend = backend,
    };

    private static readonly EncoderInfo H264Amf = Info("h264_amf", VideoCodec.H264, EncoderBackend.Amf);
    private static readonly EncoderInfo H264Nvenc = Info("h264_nvenc", VideoCodec.H264, EncoderBackend.Nvenc);
    private static readonly EncoderInfo H264Software = Info("libx264", VideoCodec.H264, EncoderBackend.Software);
    private static readonly EncoderInfo HevcAmf = Info("hevc_amf", VideoCodec.Hevc, EncoderBackend.Amf);
    private static readonly EncoderInfo HevcSoftware = Info("libx265", VideoCodec.Hevc, EncoderBackend.Software);
    private static readonly EncoderInfo Av1Software = Info("libsvtav1", VideoCodec.Av1, EncoderBackend.Software);

    private sealed class FakeEncoderProbe(params EncoderInfo[] available) : IEncoderProbe
    {
        public IReadOnlyList<EncoderInfo> GetAvailableEncoders() => available;
    }

    [Fact]
    public void Build_PutsSelectedFirst()
    {
        var chain = new EncoderFallbackChain(new FakeEncoderProbe(H264Amf, H264Nvenc, H264Software))
            .Build(H264Amf);

        Assert.Equal(H264Amf, chain[0]);
    }

    [Fact]
    public void Build_FallsBackThroughSameCodecThenSoftwareBaselineThenAnyHardwareH264()
    {
        var chain = new EncoderFallbackChain(new FakeEncoderProbe(HevcAmf, H264Nvenc, HevcSoftware, H264Software))
            .Build(HevcAmf);

        Assert.Equal([HevcAmf, HevcSoftware, H264Software, H264Nvenc], chain);
    }

    [Fact]
    public void Build_DoesNotDuplicateEntriesAcrossTiers()
    {
        // libx264 is both "same codec" and "last-resort software" — must only appear once.
        var chain = new EncoderFallbackChain(new FakeEncoderProbe(H264Amf, H264Software))
            .Build(H264Amf);

        Assert.Equal([H264Amf, H264Software], chain);
    }

    [Fact]
    public void Build_OmitsLibx264WhenNotAvailable()
    {
        var chain = new EncoderFallbackChain(new FakeEncoderProbe(Av1Software))
            .Build(Av1Software);

        Assert.Equal([Av1Software], chain);
    }

    [Fact]
    public void BuildSoftwareOnly_NeverReturnsHardwareEncoders()
    {
        var chain = new EncoderFallbackChain(new FakeEncoderProbe(H264Amf, H264Nvenc, H264Software))
            .BuildSoftwareOnly(H264Amf);

        Assert.Equal([H264Software], chain);
        Assert.All(chain, e => Assert.False(e.IsHardware));
    }

    [Fact]
    public void BuildSoftwareOnly_FallsBackToLibx264ForOtherCodecs()
    {
        var chain = new EncoderFallbackChain(new FakeEncoderProbe(Av1Software, H264Software))
            .BuildSoftwareOnly(Av1Software);

        Assert.Equal([Av1Software, H264Software], chain);
    }

    [Fact]
    public void BuildSoftwareOnly_EmptyWhenNoSoftwareEncoderAvailable()
    {
        var chain = new EncoderFallbackChain(new FakeEncoderProbe(H264Amf))
            .BuildSoftwareOnly(H264Amf);

        Assert.Empty(chain);
    }
}
