using RecMode.Encoding.Encoders;
using Xunit;

namespace RecMode.Encoding.Tests;

public class EncoderProbeTests
{
    [Fact]
    public void SelectAvailableEncoders_RequiresSoftwareEncodersToTrialOpen()
    {
        List<EncoderInfo> encoders = EncoderProbe.SelectAvailableEncoders(
            Encoders("libx264", "libx265", "libsvtav1"),
            id => id == "libx264");

        Assert.Equal(["libx264"], encoders.Select(e => e.FfmpegId));
    }

    [Fact]
    public void SelectAvailableEncoders_KeepsWorkingHardwareAndSoftware()
    {
        List<EncoderInfo> encoders = EncoderProbe.SelectAvailableEncoders(
            Encoders("h264_amf", "libx264"),
            id => id is "h264_amf" or "libx264");

        Assert.Equal(["h264_amf", "libx264"], encoders.Select(e => e.FfmpegId));
    }

    [Fact]
    public void SelectAvailableEncoders_AddsLibx264WhenItWorksEvenIfMissingFromList()
    {
        List<EncoderInfo> encoders = EncoderProbe.SelectAvailableEncoders(
            Encoders("h264_nvenc"),
            id => id == "libx264");

        Assert.Equal(["libx264"], encoders.Select(e => e.FfmpegId));
    }

    [Fact]
    public void SelectAvailableEncoders_ReturnsEmptyWhenNothingCanOpen()
    {
        List<EncoderInfo> encoders = EncoderProbe.SelectAvailableEncoders(
            Encoders("h264_nvenc", "libx264"),
            _ => false);

        Assert.Empty(encoders);
    }

    private static string Encoders(params string[] ids)
    {
        return string.Join(Environment.NewLine, ids.Select(id => $" V....D {id} test encoder"));
    }
}
