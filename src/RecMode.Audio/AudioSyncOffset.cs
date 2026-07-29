namespace RecMode.Audio;

/// <summary>
/// Converts the user's A/V sync offset in milliseconds into the concrete sample counts
/// <see cref="AudioMixer.PumpUntil"/> applies at the head of the audio stream. Extracted as a pure function
/// so the conversion — the part that's easy to get subtly wrong (sign handling, stereo alignment, rounding)
/// — is directly testable without a live mixer, a real named pipe, or a running recording.
/// </summary>
public static class AudioSyncOffset
{
    /// <summary>Bound on the offset, matching <c>RecordingCoordinator</c>'s own clamp. Far beyond any
    /// plausible capture-pipeline mismatch (ITU-R BT.1359-1 puts even the acceptability limit at roughly
    /// 90-185ms), while keeping the prepended silence bounded.</summary>
    public const int MaxOffsetMs = 500;

    /// <summary>
    /// How many interleaved floats to prepend as silence (positive offset — delays audio, for when sound
    /// runs ahead of the picture) and how many leading floats to discard (negative offset — advances audio,
    /// for when sound lags). Exactly one is ever non-zero. Both are rounded down to a whole frame so a
    /// stereo stream can never be knocked out of L/R alignment, which would swap the channels for the entire
    /// rest of the recording rather than merely shifting it.
    /// </summary>
    public static (long SilenceFloats, long DiscardFloats) ComputePlan(int offsetMs, int sampleRate, int channels)
    {
        if (sampleRate <= 0 || channels <= 0)
        {
            return (0, 0);
        }

        int clamped = Math.Clamp(offsetMs, -MaxOffsetMs, MaxOffsetMs);
        long floats = (long)(Math.Abs(clamped) / 1000.0 * sampleRate * channels);
        floats -= floats % channels; // keep frame-aligned

        return clamped > 0 ? (floats, 0) : (0, clamped < 0 ? floats : 0);
    }
}
