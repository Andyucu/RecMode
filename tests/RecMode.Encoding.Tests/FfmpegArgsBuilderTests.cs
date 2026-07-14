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
    [InlineData(100, 1)]
    [InlineData(70, 25)]
    [InlineData(50, 37)]
    public void QualityToCrf_FollowsCurvedModel(int quality, int expectedCrf)
    {
        Assert.Equal(expectedCrf, FfmpegArgsBuilder.QualityToCrf(quality));
    }

    [Fact]
    public void QualityToCrf_SvtAv1RangeReachesUpTo63()
    {
        // SVT-AV1's wider CRF range (0-63) must be reachable via the maxCrf override, not clamped to 51.
        Assert.Equal(63, FfmpegArgsBuilder.QualityToCrf(0, FfmpegArgsBuilder.MaxCrfAv1));
        Assert.Equal(1, FfmpegArgsBuilder.QualityToCrf(100, FfmpegArgsBuilder.MaxCrfAv1));
        Assert.True(FfmpegArgsBuilder.QualityToCrf(30, FfmpegArgsBuilder.MaxCrfAv1) > 51,
            "a low-quality slider value on AV1 should be able to reach above the H.264/HEVC-family ceiling of 51");
    }

    [Theory]
    [InlineData(EncoderBackend.Software, 0)]
    [InlineData(EncoderBackend.Nvenc, -2)]
    [InlineData(EncoderBackend.Amf, -2)]
    [InlineData(EncoderBackend.Qsv, -1)]
    public void EffectiveQualityValue_AppliesPerEncoderCalibration(EncoderBackend backend, int expectedOffset)
    {
        var encoder = Enc("x", VideoCodec.H264, backend);
        int baseline = FfmpegArgsBuilder.QualityToCrf(70);
        Assert.Equal(baseline + expectedOffset, FfmpegArgsBuilder.EffectiveQualityValue(encoder, 70));
    }

    [Fact]
    public void EffectiveQualityValue_ClampsCalibratedValueToValidRange()
    {
        // At quality=100 the curved CRF already sits at MinCrf (1); a negative hardware-encoder offset must
        // not push the calibrated value below 1 (which would otherwise not be a valid ffmpeg CRF/CQ/QP).
        var nvenc = Enc("h264_nvenc", VideoCodec.H264, EncoderBackend.Nvenc);
        Assert.Equal(FfmpegArgsBuilder.MinCrf, FfmpegArgsBuilder.EffectiveQualityValue(nvenc, 100));
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
        Assert.Contains("-c:v libx264 -preset veryfast -crf 25", args);
    }

    [Fact]
    public void Amf_UsesCqp()
    {
        string args = FfmpegArgsBuilder.Build(Job(Enc("h264_amf", VideoCodec.H264, EncoderBackend.Amf), quality: 70));
        Assert.Contains("-c:v h264_amf -usage transcoding -quality balanced -rc cqp -qp_i 23 -qp_p 23", args);
    }

    [Fact]
    public void Nvenc_UsesCq()
    {
        string args = FfmpegArgsBuilder.Build(Job(Enc("h264_nvenc", VideoCodec.H264, EncoderBackend.Nvenc), quality: 70));
        Assert.Contains("-c:v h264_nvenc -preset p4 -rc vbr -cq 23", args);
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
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -c:v libx264 -preset veryfast -crf 25 -pix_fmt yuv420p -movflags +faststart -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_Libx264_Mkv_VideoOnly_NoFaststart()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mkv);
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -c:v libx264 -preset veryfast -crf 25 -pix_fmt yuv420p -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_Amf_Mkv_VideoOnly()
    {
        var job = Job(Enc("h264_amf", VideoCodec.H264, EncoderBackend.Amf), MediaContainer.Mkv);
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -c:v h264_amf -usage transcoding -quality balanced -rc cqp -qp_i 23 -qp_p 23 -pix_fmt yuv420p -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_Libx264_Mp4_WithAudioAac()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mp4)
            with { AudioPipeName = "aud", AudioCodec = AudioCodec.Aac, AudioBitrateKbps = 192 };
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -f f32le -ar 48000 -ac 2 -i \\.\pipe\aud -map 0:v:0 -map 1:a:0 -c:v libx264 -preset veryfast -crf 25 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Snapshot_SvtAv1_WebM_WithAudioOpus()
    {
        var job = Job(Enc("libsvtav1", VideoCodec.Av1, EncoderBackend.Software), MediaContainer.WebM)
            with { AudioPipeName = "aud", AudioCodec = AudioCodec.Opus, AudioBitrateKbps = 192 };
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -f f32le -ar 48000 -ac 2 -i \\.\pipe\aud -map 0:v:0 -map 1:a:0 -c:v libsvtav1 -preset 8 -crf 30 -pix_fmt yuv420p -c:a libopus -b:a 192k -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Fact]
    public void Guardrail_OffByDefault_NoMaxrateInArgs()
    {
        string args = FfmpegArgsBuilder.Build(Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), quality: 70));
        Assert.DoesNotContain("-maxrate", args);
    }

    [Fact]
    public void Guardrail_WhenEnabled_AddsMaxrateAndBufsizeForSoftwareEncoder()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), quality: 70) with { BitrateGuardrailEnabled = true };
        string args = FfmpegArgsBuilder.Build(job);
        (int maxRateKbps, int bufSizeKbps) = FfmpegArgsBuilder.EstimateGuardrail(2560, 1440, 60, 70);
        Assert.Contains($"-maxrate {maxRateKbps}k -bufsize {bufSizeKbps}k", args);
    }

    [Fact]
    public void Guardrail_WhenEnabled_AddsMaxrateForNvenc()
    {
        var job = Job(Enc("h264_nvenc", VideoCodec.H264, EncoderBackend.Nvenc), quality: 70) with { BitrateGuardrailEnabled = true };
        Assert.Contains("-maxrate", FfmpegArgsBuilder.Build(job));
    }

    [Theory]
    [InlineData("h264_amf", EncoderBackend.Amf)]
    [InlineData("h264_qsv", EncoderBackend.Qsv)]
    public void Guardrail_WhenEnabled_SkipsAmfAndQsv(string ffmpegId, EncoderBackend backend)
    {
        // AMF's constant-QP mode and QSV's ICQ mode don't rate-limit under their current rc mode — adding
        // -maxrate there wouldn't do anything, so it's deliberately left out rather than emitting a no-op flag.
        var job = Job(Enc(ffmpegId, VideoCodec.H264, backend), quality: 70) with { BitrateGuardrailEnabled = true };
        Assert.DoesNotContain("-maxrate", FfmpegArgsBuilder.Build(job));
    }

    [Theory]
    [InlineData(0, "Small file")]
    [InlineData(25, "Small file")]
    [InlineData(26, "Balanced")]
    [InlineData(55, "Balanced")]
    [InlineData(56, "High quality")]
    [InlineData(85, "High quality")]
    [InlineData(86, "Visually lossless")]
    [InlineData(100, "Visually lossless")]
    public void QualityTier_BucketsAcrossTheFullRange(int quality, string expectedTier)
    {
        Assert.Equal(expectedTier, FfmpegArgsBuilder.QualityTier(quality));
    }

    [Fact]
    public void EstimateTypicalKbps_IncreasesWithQualityResolutionAndFps()
    {
        int low = FfmpegArgsBuilder.EstimateTypicalKbps(1920, 1080, 30, 10);
        int high = FfmpegArgsBuilder.EstimateTypicalKbps(1920, 1080, 30, 90);
        Assert.True(high > low, "higher quality should estimate a higher typical bitrate");

        int biggerRes = FfmpegArgsBuilder.EstimateTypicalKbps(3840, 2160, 30, 70);
        int smallerRes = FfmpegArgsBuilder.EstimateTypicalKbps(1920, 1080, 30, 70);
        Assert.True(biggerRes > smallerRes, "higher resolution should estimate a higher typical bitrate");
    }

    [Theory]
    [InlineData(EncoderEffort.Fast, "-c:v libx264 -preset ultrafast -crf 25")]
    [InlineData(EncoderEffort.Balanced, "-c:v libx264 -preset veryfast -crf 25")] // default preserved
    [InlineData(EncoderEffort.Quality, "-c:v libx264 -preset medium -crf 25")]
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
    public void Snapshot_Libx264_Mkv_WithAudioFlac()
    {
        // FLAC is valid only on MKV (verified end-to-end); it takes no bitrate and MKV gets no faststart.
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mkv)
            with { AudioPipeName = "aud", AudioCodec = AudioCodec.Flac };
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -f f32le -ar 48000 -ac 2 -i \\.\pipe\aud -map 0:v:0 -map 1:a:0 -c:v libx264 -preset veryfast -crf 25 -pix_fmt yuv420p -c:a flac -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }

    [Theory]
    [InlineData(0, 1440)]
    [InlineData(-2, 1440)]
    [InlineData(2561, 1440)]
    public void Build_RejectsInvalidWidth(int width, int height)
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)) with { Width = width, Height = height };
        Assert.Throws<ArgumentException>(() => FfmpegArgsBuilder.Build(job));
    }

    [Fact]
    public void Build_RejectsInvalidFrameRate()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)) with { FrameRate = 0 };
        Assert.Throws<ArgumentException>(() => FfmpegArgsBuilder.Build(job));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"bad\name")]
    [InlineData("bad/name")]
    public void Build_RejectsInvalidPipeName(string pipeName)
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)) with { PipeName = pipeName };
        Assert.Throws<ArgumentException>(() => FfmpegArgsBuilder.Build(job));
    }

    [Fact]
    public void Build_RejectsEmptyOutputPath()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)) with { OutputPath = "" };
        Assert.Throws<ArgumentException>(() => FfmpegArgsBuilder.Build(job));
    }

    [Fact]
    public void Build_RejectsNegativeThreadCap()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software)) with { CpuThreadCap = -1 };
        Assert.Throws<ArgumentException>(() => FfmpegArgsBuilder.Build(job));
    }

    [Fact]
    public void Snapshot_Libx264_Mp4_WithThreadCap()
    {
        var job = Job(Enc("libx264", VideoCodec.H264, EncoderBackend.Software), MediaContainer.Mp4)
            with { CpuThreadCap = 4 };
        Assert.Equal(
            @"-hide_banner -loglevel warning -f rawvideo -pix_fmt nv12 -s 2560x1440 -r 60 -i \\.\pipe\testpipe -threads 4 -c:v libx264 -preset veryfast -crf 25 -pix_fmt yuv420p -movflags +faststart -y ""C:\out\clip.mp4""",
            Norm(FfmpegArgsBuilder.Build(job)));
    }
}
