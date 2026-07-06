using RecMode.Core.Recording;
using Xunit;

namespace RecMode.Core.Tests;

public class RecordingHealthTests
{
    [Fact]
    public void KeepingUp_IsHealthy()
    {
        // 10 s in at 60 fps with ~600 frames written = on time.
        Assert.False(RecordingHealth.IsBehindRealtime(10.0, 600, 60));
        Assert.True(RecordingHealth.FramesBehind(10.0, 600, 60) <= 0);
    }

    [Fact]
    public void MoreThanOneSecondBehind_IsUnhealthy()
    {
        // 10 s in at 60 fps but only 500 frames written = 100 frames (~1.7 s) behind.
        Assert.True(RecordingHealth.IsBehindRealtime(10.0, 500, 60));
        Assert.Equal(100, RecordingHealth.FramesBehind(10.0, 500, 60));
    }

    [Fact]
    public void WithinGracePeriod_NotFlagged()
    {
        // Under 2 s, even a big gap isn't flagged (startup ramp).
        Assert.False(RecordingHealth.IsBehindRealtime(1.5, 0, 60));
    }

    [Fact]
    public void JustUnderOneSecondBehind_NotFlagged()
    {
        // 10 s at 60 fps, 550 written = 50 frames behind (< 1 s) → still healthy.
        Assert.False(RecordingHealth.IsBehindRealtime(10.0, 550, 60));
    }

    [Fact]
    public void DiskCritical_BelowThreshold()
    {
        Assert.True(RecordingHealth.IsDiskCritical(100L * 1024 * 1024));   // 100 MB < 500 MB
        Assert.True(RecordingHealth.IsDiskCritical(0));
    }

    [Fact]
    public void DiskCritical_AboveThreshold_OrUnknown()
    {
        Assert.False(RecordingHealth.IsDiskCritical(2L * 1024 * 1024 * 1024)); // 2 GB free
        Assert.False(RecordingHealth.IsDiskCritical(-1));                       // unknown reading ignored
    }

    [Fact]
    public void Downgrade_SoftwareEncoderNeverDowngrades()
    {
        // No further fallback exists once already on software — stays healthy-but-slow instead.
        Assert.False(RecordingHealth.ShouldDowngradeToSoftware(100, encoderIsHardware: false));
    }

    [Fact]
    public void Downgrade_HardwareBehindPastThreshold_Downgrades()
    {
        Assert.True(RecordingHealth.ShouldDowngradeToSoftware(RecordingHealth.DowngradeAfterSeconds + 0.1, encoderIsHardware: true));
    }

    [Fact]
    public void Downgrade_HardwareNotYetPastThreshold_NoDowngrade()
    {
        Assert.False(RecordingHealth.ShouldDowngradeToSoftware(RecordingHealth.DowngradeAfterSeconds - 0.1, encoderIsHardware: true));
    }
}
