namespace RecMode.Audio;

/// <summary>Pure DSP helpers for the mixer (testable without live audio).</summary>
public static class AudioMath
{
    /// <summary>Threshold at which the limiter's knee begins; below it, samples pass through unchanged.</summary>
    public const float SoftClipThreshold = 0.7f;

    /// <summary>
    /// Limits a summed sample toward ±1 with a tanh knee, unity gain below <see cref="SoftClipThreshold"/>.
    /// Unconditional <c>tanh(x)</c> (the previous implementation) is a waveshaper on every sample, not a
    /// limiter: even a signal that mathematically cannot clip (a single unmuted source at gain ≤ 1, which is
    /// confined to [-1, 1] by construction) still measured 2-6% THD and a ~1.5 dB level loss on a real sine —
    /// every RecMode recording carried audible, unnecessary distortion. This only engages the curve once a
    /// sample actually approaches full scale; anything under the threshold is untouched.
    /// </summary>
    public static float SoftClip(float x)
    {
        float a = MathF.Abs(x);
        if (a <= SoftClipThreshold)
        {
            return x;
        }

        float headroom = 1f - SoftClipThreshold;
        float over = a - SoftClipThreshold;
        float compressed = SoftClipThreshold + headroom * MathF.Tanh(over / headroom);
        return MathF.CopySign(compressed, x);
    }

    public static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0f;
        foreach (float s in samples)
        {
            float a = Math.Abs(s);
            if (a > peak)
            {
                peak = a;
            }
        }

        return peak;
    }

    public static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0f;
        }

        double sumSq = 0;
        foreach (float s in samples)
        {
            sumSq += s * (double)s;
        }

        return (float)Math.Sqrt(sumSq / samples.Length);
    }
}
