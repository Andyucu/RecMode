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

    [Fact]
    public void AudioCodec_SteeredByContainer()
    {
        // MP4/MOV → AAC; MKV → requested; WebM → Opus (plan §3.3).
        Assert.Equal("-c:a aac -b:a 192k", FfmpegArgsBuilder.BuildAudioArgs(MediaContainer.Mp4, AudioCodec.Aac, 192));
        Assert.Equal("-c:a aac -b:a 192k", FfmpegArgsBuilder.BuildAudioArgs(MediaContainer.Mp4, AudioCodec.Opus, 192)); // MP4 can't Opus
        Assert.Equal("-c:a libopus -b:a 128k", FfmpegArgsBuilder.BuildAudioArgs(MediaContainer.Mkv, AudioCodec.Opus, 128));
        Assert.Equal("-c:a libopus -b:a 128k", FfmpegArgsBuilder.BuildAudioArgs(MediaContainer.WebM, AudioCodec.Aac, 128)); // WebM → Opus
        Assert.Equal("-c:a flac", FfmpegArgsBuilder.BuildAudioArgs(MediaContainer.Mkv, AudioCodec.Flac, 192));
    }

    [Fact]
    public void ThreadCap_AppliesToSoftwareOnly()
    {
        var sw = Enc("libx264", VideoCodec.H264, EncoderBackend.Software);
        var hw = Enc("h264_amf", VideoCodec.H264, EncoderBackend.Amf);

        Assert.Contains("-threads 4", FfmpegArgsBuilder.Build(Job(sw) with { CpuThreadCap = 4 }));
        Assert.DoesNotContain("-threads", FfmpegArgsBuilder.Build(Job(sw) with { CpuThreadCap = 0 })); // 0 = auto
        Assert.DoesNotContain("-threads", FfmpegArgsBuilder.Build(Job(hw) with { CpuThreadCap = 4 })); // hw ignores it
    }

    [Fact]
    public void AudioInput_AddedWhenPipeConfigured()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)) with { AudioPipeName = "aud" };
        string args = FfmpegArgsBuilder.Build(job);
        Assert.Contains("-f f32le -ar 48000 -ac 2 -i", args);
        Assert.Contains("-map 0:v:0 -map 1:a:0", args);
    }

    // ---- Snapshot matrix (plan §3.3): lock the full command line so an ffmpeg re-pin or a careless edit can't
    // silently change the encode. Update these strings deliberately when an argument genuinely changes. ----

    private static string Norm(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    [Fact]
    public void Snapshot_Libx264_Mp4_VideoOnly()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mp4);
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -c:v libx264 -preset veryfast -crf 24 -pix_fmt yuv420p -movflags +faststart -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_Libx264_Mkv_VideoOnly_NoFaststart()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mkv);
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -c:v libx264 -preset veryfast -crf 24 -pix_fmt yuv420p -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_Amf_Mkv_VideoOnly()
    {
        var job = Job(Enc("h264_amf", VideoCodec.H264, EncoderBackend.Amf), MediaContainer.Mkv);
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -c:v h264_amf -usage transcoding -quality balanced -rc cqp -qp_i 24 -qp_p 24 -pix_fmt yuv420p -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_Libx264_Mp4_WithAudioAac()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mp4)
            with { AudioPipeName = "aud", AudioCodec = AudioCodec.Aac, AudioBitrateKbps = 192 };
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -f f32le -ar 48000 -ac 2 -i \\.\pipe\aud -map 0:v:0 -map 1:a:0 -c:v libx264 -preset veryfast -crf 24 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_SvtAv1_WebM_WithAudioOpus()
    {
        var job = Job(Enc("libsvtav1", VideoCodec.Av1, EncoderBackend.Software), MediaContainer.WebM)
            with { AudioPipeName = "aud", AudioCodec = AudioCodec.Opus, AudioBitrateKbps = 192 };
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -f f32le -ar 48000 -ac 2 -i \\.\pipe\aud -map 0:v:0 -map 1:a:0 -c:v libsvtav1 -preset 8 -crf 24 -pix_fmt yuv420p -c:a libopus -b:a 192k -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Theory]
    [InlineData(EncoderEffort.Fast, "-c:v libx264 -preset ultrafast -crf 24")]
    [InlineData(EncoderEffort.Balanced, "-c:v libx264 -preset veryfast -crf 24")] // default preserved
    [InlineData(EncoderEffort.Quality, "-c:v libx264 -preset medium -crf 24")]
    public void Effort_MapsX264Preset(EncoderEffort effort, string expected)
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)) with { Effort = effort };
        Assert.Contains(expected, FfmpegArgsBuilder.Build(job));
    }

    [Theory]
    [InlineData(EncoderEffort.Fast, "-preset p2")]
    [InlineData(EncoderEffort.Balanced, "-preset p4")] // default preserved
    [InlineData(EncoderEffort.Quality, "-preset p6")]
    public void Effort_MapsNvencPreset(EncoderEffort effort, string expected)
    {
        var job = Job(Enc("h264_nvenc", VideoCodec.H264, EncoderBackend.Nvenc)) with { Effort = effort };
        Assert.Contains(expected, FfmpegArgsBuilder.Build(job));
    }

    [Theory]
    [InlineData(EncoderEffort.Fast, "-quality speed")]
    [InlineData(EncoderEffort.Balanced, "-quality balanced")] // default preserved
    [InlineData(EncoderEffort.Quality, "-quality quality")]
    public void Effort_MapsAmfQuality(EncoderEffort effort, string expected)
    {
        var job = Job(Enc("h264_amf", VideoCodec.H264, EncoderBackend.Amf)) with { Effort = effort };
        Assert.Contains(expected, FfmpegArgsBuilder.Build(job));
    }

    [Fact]
    public void Snapshot_Libx264_Mp4_WithThreadCap()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mp4)
            with { CpuThreadCap = 4 };
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -threads 4 -c:v libx264 -preset veryfast -crf 24 -pix_fmt yuv420p -movflags +faststart -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }
}
