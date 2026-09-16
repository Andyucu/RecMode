namespace RecMode.Audio;

/// <summary>
/// Microphone cleanup for the mix path: a 90 Hz high-pass (kills rumble, desk thumps, fan low-end) plus an
/// adaptive downward expander that mutes steady background noise between speech.
/// <para>
/// <b>Deliberately zero-latency.</b> A frequency-domain denoiser (ffmpeg's <c>afftdn</c>, or an STFT spectral
/// subtraction written here) needs one analysis window of lookahead to reconstruct its overlap-add output —
/// roughly 21 ms at 1024 samples/48 kHz. That is a real A/V sync shift, and this project treats sync as a
/// hard constraint (a whole feature decision was made around a ≤45 ms bound). So this is a sample-by-sample
/// expander instead: no lookahead, no buffering, no latency, no model file, nothing to bundle. The trade is
/// that it cannot remove noise *during* speech the way a spectral method can — it removes the steady
/// background in the gaps, which is the audible complaint a screen recording actually has.
/// </para>
/// <para>
/// The expander's threshold tracks an estimated noise floor, so it needs no user calibration: the floor falls
/// quickly toward quiet content and rises very slowly, meaning it settles on the background rather than on the
/// speaker. <see cref="Process"/> is not thread-safe — the mixer's pump thread is its single caller.
/// </para>
/// </summary>
internal sealed class MicNoiseSuppressor
{
    private const int Channels = 2;
    private const double SampleRate = 48000;

    private const double HighPassHz = 90;

    // Envelope follower: fast to open (speech onset must not be clipped), slow to close (keeps the tail of a
    // word intact instead of chopping it).
    private const double EnvelopeAttackSeconds = 0.002;
    private const double EnvelopeReleaseSeconds = 0.060;

    // Gate gain smoothing. Opening fast avoids swallowing the first syllable; closing slower than the
    // envelope's own release avoids an audible "pumping" on every inter-word gap.
    private const double GainOpenSeconds = 0.004;
    private const double GainCloseSeconds = 0.250;

    // The estimated floor drops toward the current envelope quickly (so it follows a background that just got
    // quieter) and rises only while the envelope is itself near the floor — i.e. during what already looks
    // like background. Speech never drags the estimate up with it, which is the failure mode of a plain
    // two-time-constant tracker: talk continuously for a few seconds and the "floor" climbs to speech level,
    // after which the gate starts eating the speech.
    private const double FloorFallPerSample = 0.002;      // ~10 ms
    private const double FloorRisePerSample = 0.00001;    // ~2 s
    private const double BackgroundBandRatio = 1.5;       // "still looks like background" ceiling

    /// <summary>Length of the initial floor-bootstrap window (~100 ms) — the floor starts at the quietest
    /// envelope seen in it, so a recording that opens mid-speech doesn't latch onto that speech.</summary>
    private const int BootstrapFrames = 4800;

    // How far above the estimated floor a signal must sit to open the gate (≈ +6 dB). Low enough that quiet
    // speech still opens it, high enough that noise fluctuation doesn't chatter it.
    private const double OpenThresholdRatio = 2.0;

    private readonly double _attackCoeff;
    private readonly double _releaseCoeff;
    private readonly double _gainOpenCoeff;
    private readonly double _gainCloseCoeff;

    // High-pass biquad (RBJ), per channel.
    private readonly double _b0, _b1, _b2, _a1, _a2;
    private readonly double[] _x1 = new double[Channels];
    private readonly double[] _x2 = new double[Channels];
    private readonly double[] _y1 = new double[Channels];
    private readonly double[] _y2 = new double[Channels];

    private double _noiseFloor;
    private double _envelope;
    private double _gain = 1.0;
    private int _bootstrapFrames;
    private double _bootstrapMin = double.MaxValue;

    public MicNoiseSuppressor()
    {
        _attackCoeff = Coefficient(EnvelopeAttackSeconds);
        _releaseCoeff = Coefficient(EnvelopeReleaseSeconds);
        _gainOpenCoeff = Coefficient(GainOpenSeconds);
        _gainCloseCoeff = Coefficient(GainCloseSeconds);

        double wc = 2 * Math.PI * HighPassHz / SampleRate;
        double cos = Math.Cos(wc);
        double alpha = Math.Sin(wc) / (2 * 0.7071);
        double a0 = 1 + alpha;
        _b0 = (1 + cos) / 2 / a0;
        _b1 = -(1 + cos) / a0;
        _b2 = (1 + cos) / 2 / a0;
        _a1 = -2 * cos / a0;
        _a2 = (1 - alpha) / a0;
    }

    /// <summary>Strength 0–100. 0 is a no-op (also short-circuited by the mixer, so the DSP never runs when
    /// the feature is off); 100 gates to about -30 dB. Linear in between.</summary>
    public static bool IsEnabled(int strength) => strength > 0;

    /// <summary>Highest attenuation the gate can apply, as a linear gain, for a given strength.</summary>
    internal static double AttenuationFor(int strength)
    {
        int clamped = Math.Clamp(strength, 0, 100);
        if (clamped == 0)
        {
            return 1.0; // off is a no-op, never a (smaller) reduction
        }

        double depthDb = 6 + clamped * 0.24; // -6 dB .. -30 dB
        return Math.Pow(10, -depthDb / 20);
    }

    /// <summary>Clears filter and envelope state. Called when the mixer (re)starts a source, so a fresh
    /// recording never inherits a previous one's estimated noise floor.</summary>
    public void Reset()
    {
        Array.Clear(_x1);
        Array.Clear(_x2);
        Array.Clear(_y1);
        Array.Clear(_y2);
        _noiseFloor = 0;
        _envelope = 0;
        _gain = 1.0;
        _bootstrapFrames = 0;
        _bootstrapMin = double.MaxValue;
    }

    /// <summary>Processes <paramref name="count"/> interleaved stereo samples in place.</summary>
    public void Process(float[] buffer, int count, int strength)
    {
        if (!IsEnabled(strength) || count <= 0)
        {
            return;
        }

        double floorGain = AttenuationFor(strength);
        int frames = count / Channels;

        for (int frame = 0; frame < frames; frame++)
        {
            int at = frame * Channels;
            double peak = 0;
            for (int channel = 0; channel < Channels; channel++)
            {
                double filtered = HighPass(buffer[at + channel], channel);
                buffer[at + channel] = (float)filtered;
                double magnitude = Math.Abs(filtered);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            // Envelope of the (pre-gain — never feedback from the gate's own output) signal, then the adaptive
            // floor. The floor is bootstrapped from the MINIMUM envelope seen over the first ~100 ms, so a
            // recording that opens on speech doesn't latch the floor onto that speech; after that it drops
            // fast toward quieter content and creeps up only while the signal already looks like background.
            if (_bootstrapFrames == 0)
            {
                // Seed the follower at the real magnitude: a zero-seeded one ramps up over ~10 ms, and those
                // first tiny values would become the bootstrap minimum — i.e. a floor near zero, i.e. a gate
                // that never closes (found by the first version of this test failing open).
                _envelope = peak;
            }

            double envelopeCoeff = peak > _envelope ? _attackCoeff : _releaseCoeff;
            _envelope += (peak - _envelope) * envelopeCoeff;

            if (_bootstrapFrames < BootstrapFrames)
            {
                _bootstrapFrames++;
                _bootstrapMin = Math.Min(_bootstrapMin, _envelope);
                _noiseFloor = _bootstrapMin;
            }
            else if (_envelope < _noiseFloor)
            {
                _noiseFloor += (_envelope - _noiseFloor) * FloorFallPerSample;
            }
            else if (_envelope < _noiseFloor * BackgroundBandRatio)
            {
                _noiseFloor += (_envelope - _noiseFloor) * FloorRisePerSample;
            }

            double target = _envelope > _noiseFloor * OpenThresholdRatio ? 1.0 : floorGain;
            double gainCoeff = target > _gain ? _gainOpenCoeff : _gainCloseCoeff;
            _gain += (target - _gain) * gainCoeff;

            float applied = (float)_gain;
            for (int channel = 0; channel < Channels; channel++)
            {
                buffer[at + channel] *= applied;
            }
        }
    }

    private double HighPass(double input, int channel)
    {
        double output = _b0 * input + _b1 * _x1[channel] + _b2 * _x2[channel] - _a1 * _y1[channel] - _a2 * _y2[channel];
        _x2[channel] = _x1[channel];
        _x1[channel] = input;
        _y2[channel] = _y1[channel];
        _y1[channel] = output;
        return output;
    }

    /// <summary>One-pole smoothing coefficient for a given time constant, per sample.</summary>
    private static double Coefficient(double seconds) =>
        1 - Math.Exp(-1.0 / (seconds * SampleRate));
}
