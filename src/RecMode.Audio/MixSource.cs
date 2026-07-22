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
        WaveFormat f = capture.WaveFormat;
        _channels = f.Channels;
        _isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat
            || (f.Encoding == WaveFormatEncoding.Extensible && f.BitsPerSample == 32);

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
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
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
            for (int i = 0; i < totalSamples; i++)
            {
                short s = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
                floats[i] = s / 32768f;
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
