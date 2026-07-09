using RecMode.App.Services;
using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using Xunit;

namespace RecMode.App.Tests;

public class CaptureSizingTests
{
    private static EncoderInfo Info(VideoCodec codec, EncoderBackend backend) => new()
    {
        FfmpegId = "id",
        DisplayName = "id",
        Codec = codec,
        Backend = backend,
    };

    private static readonly EncoderInfo HardwareH264 = Info(VideoCodec.H264, EncoderBackend.Amf);
    private static readonly EncoderInfo SoftwareH264 = Info(VideoCodec.H264, EncoderBackend.Software);
    private static readonly EncoderInfo HardwareHevc = Info(VideoCodec.Hevc, EncoderBackend.Nvenc);

    [Fact]
    public void Resolve_KeepsNativeSizeWhenUnderTheHardwareH264Cap()
    {
        (int w, int h) = CaptureSizing.Resolve(2560, 1440, HardwareH264);
        Assert.Equal((2560, 1440), (w, h));
    }

    [Fact]
    public void Resolve_ScalesDownAspectPreservingAboveTheHardwareH264Cap()
    {
        (int w, int h) = CaptureSizing.Resolve(5120, 1440, HardwareH264);
        Assert.Equal(4096, w);
        Assert.Equal(1152, h); // 1440 * (4096/5120) = 1152 exactly
    }

    [Fact]
    public void Resolve_MakesOddScaledDimensionsEven()
    {
        // 5121 wide -> scale 4096/5121 -> height 1439.718... rounds to 1440 (even, no adjustment needed);
        // pick a case that actually lands on an odd result instead.
        (int w, int h) = CaptureSizing.Resolve(4097, 1441, HardwareH264);
        Assert.Equal(0, w % 2);
        Assert.Equal(0, h % 2);
    }

    [Fact]
    public void Resolve_DoesNotCapSoftwareH264()
    {
        (int w, int h) = CaptureSizing.Resolve(5120, 1440, SoftwareH264);
        Assert.Equal((5120, 1440), (w, h));
    }

    [Fact]
    public void Resolve_DoesNotCapHardwareNonH264Codecs()
    {
        (int w, int h) = CaptureSizing.Resolve(7680, 2160, HardwareHevc);
        Assert.Equal((7680, 2160), (w, h));
    }

    [Fact]
    public void Resolve_MakesOddNativeDimensionsEven()
    {
        (int w, int h) = CaptureSizing.Resolve(1921, 1081, SoftwareH264);
        Assert.Equal(1920, w);
        Assert.Equal(1080, h);
    }
}
