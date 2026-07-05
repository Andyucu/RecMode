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
}
