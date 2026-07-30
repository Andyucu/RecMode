using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;

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
    // True for mixers that only ever feed live UI meters (RecordViewModel's own meter mixer, separate from
    // the one a real recording pumps) — nothing will ever call ReadMixed, so buffering samples into _buffer
    // every callback is pure waste: it silently fills for 2s then starts discarding via DiscardOnBufferOverflow.
    private readonly bool _meteringOnly;

    private volatile float _peak;
    private volatile float _rms;
    // Reused across DataAvailable callbacks (~100/s at typical WASAPI buffer sizes) instead of allocating
    // fresh arrays every time; grown, never shrunk. Only the non-float PCM path needs these — the float path
    // feeds WASAPI's own buffer straight through (see OnDataAvailable).
    private float[] _floatScratch = [];
    private byte[] _byteScratch = [];

    public float Gain { get; set; } = 1f;
    public bool Muted { get; set; }

    public AudioLevel Level => Muted ? AudioLevel.Silent : new AudioLevel(_rms, _peak);

    /// <summary>True once the underlying WASAPI capture has stopped due to a genuine device failure (device
    /// unplugged, exclusive-mode conflict, endpoint invalidated) rather than a normal Stop() call. Previously
    /// this was logged only — the mixer silently kept "reading" (zero-filled) silence from the dead source
    /// for the rest of the recording, producing a valid-but-silent stream with no indication anything failed
    /// (the same failure shape as the historical full-system-audio-silence bug, just triggered mid-recording
    /// instead of at start). Polled by the recording pacer so it can actually surface a warning.</summary>
    public bool Faulted { get; private set; }

    /// <summary>
    /// Resolves a WASAPI capture's reported format to one whose <see cref="WaveFormat.Encoding"/> can be
    /// trusted. Mix formats — full-system loopback in particular, since <c>WasapiLoopbackCapture.WaveFormat</c>
    /// reflects whatever the audio engine's actual mix format is — very commonly arrive as
    /// <see cref="WaveFormatExtensible"/>, which carries the *real* encoding (PCM vs IEEE float) in its
    /// SubFormat GUID rather than in <c>BitsPerSample</c>. This originally guessed "32-bit Extensible must be
    /// float", which silently bit-reinterpreted 32-bit integer PCM as float garbage on any device whose mix
    /// format genuinely is 32-bit PCM — the suspected cause of the long-standing full-system-audio-silence
    /// bug. Per-app audio never hit it because <c>ProcessLoopbackCapture</c> always reports a plain,
    /// unambiguous IeeeFloat format.
    /// <para>Extracted from the constructor and made internal specifically so this is unit-testable: the
    /// failing case needs hardware whose mix format is Extensible+PCM, which this project has never had
    /// access to, but a fake <see cref="IWaveIn"/> reproduces it exactly.</para>
    /// </summary>
    internal static WaveFormat ResolveFormat(WaveFormat raw) =>
        raw is WaveFormatExtensible extensible ? extensible.ToStandardWaveFormat() : raw;

    public MixSource(IWaveIn capture, bool meteringOnly = false)
    {
        _capture = capture;
        _meteringOnly = meteringOnly;

        WaveFormat f = ResolveFormat(capture.WaveFormat);
        _sourceFormat = f;
        _channels = f.Channels;
        _isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat;

        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(f.SampleRate, f.Channels))
        {
            // Metering-only mixers never drain this (nothing ever calls ReadMixed), so keep it minimal — it
            // only bounds memory, not preserves audio, for a buffer nobody reads. A real recording mixer's
            // pump thread stops briefly during a segment rotation (finalize + safe-remux + new-encoder-start
            // can legitimately take tens of seconds on a slow disk); WASAPI capture itself never stops, so
            // without real headroom here that whole gap of genuine audio silently discards via
            // DiscardOnBufferOverflow instead of landing a few seconds late once the pump resumes.
            BufferDuration = meteringOnly ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(60),
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
        // Log-only: a mid-capture WASAPI failure (device unplugged, exclusive-mode conflict from another
        // app, etc.) previously surfaced only as DataAvailable silently stopping — the underlying exception
        // was captured by the capture source and raised via this exact event, but nothing here was
        // listening for it. Doesn't change behavior (the mixer already treats "no more data" as silence);
        // this just makes the reason visible in the log instead of a mystery.
        capture.RecordingStopped += OnRecordingStopped;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Warning(e.Exception, "Audio capture stopped unexpectedly");
            Faulted = true;
        }
    }

    public void Start() => _capture.StartRecording();

    /// <summary>Discards whatever's currently buffered without stopping capture — see
    /// <see cref="RecMode.Audio.IAudioMixer.ClearBuffers"/> for why this exists.</summary>
    public void ClearBuffer() => _buffer.ClearBuffer();

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

        if (_isFloat)
        {
            // Already the exact byte layout the mix buffer expects (float32, same channel count/rate as the
            // source) — meter directly off WASAPI's own buffer and feed it straight through, instead of
            // allocating a scratch copy every callback just to hand back identical bytes.
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, totalSamples * 4));
            _peak = AudioMath.Peak(samples);
            _rms = AudioMath.Rms(samples);
            if (!_meteringOnly)
            {
                _buffer.AddSamples(e.Buffer, 0, totalSamples * 4);
            }
            return;
        }

        // General integer-PCM reader: the previous code only ever read 16-bit samples regardless of the
        // source's actual bit depth, which silently misaligned/garbled anything that wasn't exactly 16-bit.
        if (_floatScratch.Length < totalSamples)
        {
            _floatScratch = new float[totalSamples];
            _byteScratch = new byte[totalSamples * 4];
        }
        for (int i = 0; i < totalSamples; i++)
        {
            _floatScratch[i] = ReadPcmSample(e.Buffer, i * bytesPerSample, bytesPerSample);
        }

        // Meter over all channels.
        ReadOnlySpan<float> floatSpan = _floatScratch.AsSpan(0, totalSamples);
        _peak = AudioMath.Peak(floatSpan);
        _rms = AudioMath.Rms(floatSpan);

        // Feed the mix buffer (float bytes match the buffer format).
        if (!_meteringOnly)
        {
            Buffer.BlockCopy(_floatScratch, 0, _byteScratch, 0, totalSamples * 4);
            _buffer.AddSamples(_byteScratch, 0, totalSamples * 4);
        }
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
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
        // Reused across reads rather than allocated per read; grown, never shrunk. Read() runs on the audio
        // pump path (4096-float chunks, ~21 ms apart) for the whole recording whenever the WASAPI mix format
        // has more than 2 channels — routine on HDMI/receiver/5.1 setups. Same §3.9 allocation-free hot-path
        // rule as the rest of MixSource. Not thread-safe, but neither is ISampleProvider: the mixer's pump
        // is the single reader.
        private float[] _input = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            int needed = frames * channels;
            if (_input.Length < needed)
            {
                _input = new float[needed];
            }
            float[] input = _input;
            int read = source.Read(input, 0, needed);
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
