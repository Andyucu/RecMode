namespace RecMode.Audio;

/// <summary>A source's live loudness (0..1), for the meter bars. RMS drives the bar; Peak the tick.</summary>
public readonly record struct AudioLevel(float Rms, float Peak)
{
    public static readonly AudioLevel Silent = new(0f, 0f);
}
