using RecMode.Core.Settings;
using RecMode.Encoding.Encoders;
using RecMode.Encoding.Ffmpeg;
using Xunit;

namespace RecMode.Encoding.Tests;

public class FfmpegArgsBuilderTests
{
    private static EncoderInfo Enc(string id, VideoCodec codec, EncoderBackend backend) =>
        new() { FfmpegId = id, DisplayName = id, Codec = codec, Backend = backend };

    private static FfmpegJob Job(EncoderInfo enc, MediaContainer container = MediaContainer.Mp4, int quality = 70) => new()
    {
        Encoder = enc,
        Container = container,
        Width = 2560,
        Height = 1440,
        FrameRate = 60,
        Quality = quality,
        PipeName = "testpipe",
        OutputPath = @"C:\out\clip.mp4",
    };

    [Theory]
    [InlineData(0, 51)]
    [InlineData(100, 13)]
    [InlineData(70, 24)]
    public void QualityToCrf_FollowsDesignModel(int quality, int expectedCrf)
    {
        Assert.Equal(expectedCrf, FfmpegArgsBuilder.QualityToCrf(quality));
    }

    [Fact]
    public void Input_DeclaresNv12RawVideoOverThePipe()
    {
        string args = FfmpegArgsBuilder.Build(Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)));
        Assert.Contains("-f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60", args);
        Assert.Contains(@"-i \\.\pipe\testpipe", args);
    }

    [Fact]
    public void Software_UsesCrf()
    {
        string args = FfmpegArgsBuilder.Build(Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), quality: 70));
        Assert.Contains("-c:v libx264 -preset veryfast -crf 24", args);
    }

    [Fact]
    public void Amf_UsesCqp()
    {
        string args = FfmpegArgsBuilder.Build(Job(Enc("h264_amf", VideoCodec.H264, EncoderBackend.Amf), quality: 70));
        Assert.Contains("-c:v h264_amf -usage transcoding -quality balanced -rc cqp -qp_i 24 -qp_p 24", args);
    }

    [Fact]
    public void Nvenc_UsesCq()
    {
        string args = FfmpegArgsBuilder.Build(Job(Enc("h264_nvenc", VideoCodec.H264, EncoderBackend.Nvenc), quality: 70));
        Assert.Contains("-c:v h264_nvenc -preset p4 -rc vbr -cq 24", args);
    }

    [Fact]
    public void Mp4_AddsFaststart_MkvDoesNot()
    {
        var enc = Enc("libx264", VideoCodec.H264, EncoderBackend.Software);
        Assert.Contains("+faststart", FfmpegArgsBuilder.Build(Job(enc, MediaContainer.Mp4)));
        Assert.DoesNotContain("+faststart", FfmpegArgsBuilder.Build(Job(enc, MediaContainer.Mkv)));
    }
}
