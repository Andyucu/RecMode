namespace RecMode.Audio;

/// <summary>Pure DSP helpers for the mixer (testable without live audio).</summary>
public static class AudioMath
{
    /// <summary>Soft-clip a summed sample with tanh — near-unity for small signals, smooth saturation past ~1.</summary>
    public static float SoftClip(float x) => MathF.Tanh(x);

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
