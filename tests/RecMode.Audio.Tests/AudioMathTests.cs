using RecMode.Audio;
using Xunit;

namespace RecMode.Audio.Tests;

public class AudioMathTests
{
    [Fact]
    public void SoftClip_IsNearUnityForSmallSignals()
    {
        Assert.InRange(AudioMath.SoftClip(0.1f), 0.095f, 0.1f);
        Assert.Equal(0f, AudioMath.SoftClip(0f));
    }

    [Fact]
    public void SoftClip_SaturatesLargeSignals()
    {
        Assert.InRange(AudioMath.SoftClip(5f), 0.99f, 1f);
        Assert.InRange(AudioMath.SoftClip(-5f), -1f, -0.99f);
    }

    [Fact]
    public void Peak_ReturnsMaxAbsolute()
    {
        Assert.Equal(0.8f, AudioMath.Peak([0.1f, -0.8f, 0.3f]), 3);
    }

    [Fact]
    public void Rms_OfFullScaleSquareWaveIsOne()
    {
        Assert.Equal(1f, AudioMath.Rms([1f, -1f, 1f, -1f]), 3);
    }

    [Fact]
    public void Rms_OfSilenceIsZero()
    {
        Assert.Equal(0f, AudioMath.Rms([0f, 0f, 0f]));
        Assert.Equal(0f, AudioMath.Rms([]));
    }

    [Fact]
    public void Rms_OfHalfScaleIsHalf()
    {
        Assert.Equal(0.5f, AudioMath.Rms([0.5f, -0.5f, 0.5f, -0.5f]), 3);
    }
}
