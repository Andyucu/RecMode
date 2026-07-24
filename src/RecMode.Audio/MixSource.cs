using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RecMode.Audio;

/// <summary>
/// One capture source (system loopback or mic) normalized to 48 kHz stereo f32 for the mixer. Computes its
/// meter on the audio callback thread; buffers samples for the mix pump (bounded + discard-on-overflow so
/// metering-only mode doesn't grow unbounded).
/// </summary>
internal sealed class MixSource : IDisposable
{
    private const int TargetRate = 48000;

    private readonly IWaveIn _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _out;
    private readonly WaveFormat _sourceFormat;
    private readonly bool _isFloat;
    private readonly int _channels;

    private volatile float _peak;
    private volatile float _rms;

    public float Gain { get; set; } = 1f;
    public bool Muted { get; set; }

    public AudioLevel Level => Muted ? AudioLevel.Silent : new AudioLevel(_rms, _peak);

    public MixSource(IWaveIn capture)
    {
        _capture = capture;

        // WASAPI mix formats (full-system loopback in particular — WasapiLoopbackCapture.WaveFormat reflects
        // whatever the audio engine's actual mix format is) very commonly arrive as WaveFormatExtensible rather
        // than a plain WaveFormat, and Extensible carries the *real* encoding (PCM vs IEEE float) in its
        // SubFormat GUID, not in BitsPerSample. Resolving it via NAudio's own ToStandardWaveFormat() (rather
        // than guessing "32-bit Extensible must be float", which silently bit-reinterprets 32-bit integer PCM
        // as float garbage whenever a device's mix format actually is 32-bit PCM) is what was missing here —
        // per-app audio never hit this because ProcessLoopbackCapture always reports a plain, unambiguous
        // IeeeFloat WaveFormat, never Extensible.
        WaveFormat raw = capture.WaveFormat;
        WaveFormat f = raw is WaveFormatExtensible wfe ? wfe.ToStandardWaveFormat() : raw;
        _sourceFormat = f;
        _channels = f.Channels;
        _isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat;

        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(f.SampleRate, f.Channels))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
        };

        ISampleProvider sp = _buffer.ToSampleProvider();
        if (f.Channels == 1)
        {
            sp = new MonoToStereoSampleProvider(sp);
        }
        else if (f.Channels != 2)
        {
            sp = new StereoDownmixSampleProvider(sp, f.Channels);
        }
        if (f.SampleRate != TargetRate)
        {
            sp = new WdlResamplingSampleProvider(sp, TargetRate);
        }
        _out = sp; // 48 kHz stereo float

        capture.DataAvailable += OnDataAvailable;
    }

    public void Start() => _capture.StartRecording();

    /// <summary>Reads up to <paramref name="count"/> interleaved stereo floats into <paramref name="dest"/>; returns the count read (rest is silence).</summary>
    public int ReadMixed(float[] dest, int count) => _out.Read(dest, 0, count);

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytesPerSample = _sourceFormat.BitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            _peak = 0;
            _rms = 0;
            return;
        }

        int totalSamples = e.BytesRecorded / bytesPerSample;
        if (totalSamples == 0)
        {
            _peak = 0;
            _rms = 0;
            return;
        }

        float[] floats = new float[totalSamples];
        if (_isFloat)
        {
            Buffer.BlockCopy(e.Buffer, 0, floats, 0, totalSamples * 4);
        }
        else
        {
            // General integer-PCM reader: the previous code only ever read 16-bit samples regardless of the
            // source's actual bit depth, which silently misaligned/garbled anything that wasn't exactly 16-bit.
            for (int i = 0; i < totalSamples; i++)
            {
                floats[i] = ReadPcmSample(e.Buffer, i * bytesPerSample, bytesPerSample);
            }
        }

        // Meter over all channels.
        _peak = AudioMath.Peak(floats);
        _rms = AudioMath.Rms(floats);

        // Feed the mix buffer (float bytes match the buffer format).
        byte[] bytes = new byte[totalSamples * 4];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        _buffer.AddSamples(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        try { _capture.StopRecording(); } catch (Exception) { }
        _capture.Dispose();
    }

    /// <summary>Reads one little-endian signed-integer PCM sample (8/16/24/32-bit) starting at
    /// <paramref name="byteOffset"/> and normalizes it to -1..1. 8-bit PCM is the one unsigned case (centered
    /// on 128, per the WAV/WASAPI convention); everything else is signed two's-complement.</summary>
    internal static float ReadPcmSample(byte[] buffer, int byteOffset, int bytesPerSample)
    {
        switch (bytesPerSample)
        {
            case 1:
                return (buffer[byteOffset] - 128) / 128f;
            case 2:
                short s16 = (short)(buffer[byteOffset] | (buffer[byteOffset + 1] << 8));
                return s16 / 32768f;
            case 3:
                int s24 = buffer[byteOffset] | (buffer[byteOffset + 1] << 8) | (buffer[byteOffset + 2] << 16);
                if ((s24 & 0x0080_0000) != 0)
                {
                    s24 |= unchecked((int)0xFF00_0000); // sign-extend the 24-bit value into a 32-bit int
                }
                return s24 / 8_388_608f;
            case 4:
                int s32 = buffer[byteOffset] | (buffer[byteOffset + 1] << 8) |
                    (buffer[byteOffset + 2] << 16) | (buffer[byteOffset + 3] << 24);
                return s32 / 2_147_483_648f;
            default:
                return 0f;
        }
    }

    private sealed class StereoDownmixSampleProvider(ISampleProvider source, int channels) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            float[] input = new float[frames * channels];
            int read = source.Read(input, 0, input.Length);
            int inputFrames = read / channels;
            for (int frame = 0; frame < inputFrames; frame++)
            {
                int at = frame * channels;
                // Preserve front L/R, folding remaining channels equally into both sides at a safe gain.
                float left = input[at], right = input[at + 1];
                for (int channel = 2; channel < channels; channel++) { float v = input[at + channel] * 0.5f; left += v; right += v; }
                buffer[offset + frame * 2] = left;
                buffer[offset + frame * 2 + 1] = right;
            }
            return inputFrames * 2;
        }
    }
}
