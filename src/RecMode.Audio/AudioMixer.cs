using System.IO.Pipes;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecMode.Audio;

/// <summary>
/// Default <see cref="IAudioMixer"/>. System loopback + mic normalized to 48 kHz stereo f32, summed with
/// per-source gain/mute and a tanh soft-clip. The pump paces the mixed output to the wall clock and pads
/// silence on underflow (WASAPI delivers nothing during silence), keeping audio aligned to the QPC timeline.
/// </summary>
public sealed class AudioMixer : IAudioMixer
{
    public const int Rate = 48000;
    public const int Chans = 2;
    private const int BytesPerSecond = Rate * Chans * 4;

    private MixSource? _system;
    private MixSource? _mic;

    public bool IsRunning { get; private set; }
    public int SampleRate => Rate;
    public int Channels => Chans;

    public bool SystemEnabled => _system is not null;
    public bool MicEnabled => _mic is not null;

    public float SystemGain { get => _system?.Gain ?? 1f; set { if (_system is not null) _system.Gain = value; } }
    public bool SystemMuted { get => _system?.Muted ?? true; set { if (_system is not null) _system.Muted = value; } }
    public float MicGain { get => _mic?.Gain ?? 1f; set { if (_mic is not null) _mic.Gain = value; } }
    public bool MicMuted { get => _mic?.Muted ?? true; set { if (_mic is not null) _mic.Muted = value; } }

    public AudioLevel SystemLevel => _system?.Level ?? AudioLevel.Silent;
    public AudioLevel MicLevel => _mic?.Level ?? AudioLevel.Silent;

    public AudioMixerStartResult Start(bool captureSystem, bool captureMic, int? targetProcessId = null)
    {
        if (IsRunning)
        {
            Stop();
        }

        if (captureSystem)
        {
            try
            {
                IWaveIn loopback = targetProcessId is int pid
                    ? new ProcessLoopback.ProcessLoopbackCapture(pid)
                    : new WasapiLoopbackCapture();
                _system = new MixSource(loopback);
                _system.Start();
            }
            catch (Exception) when (targetProcessId is not null)
            {
                // Target process gone/activation failed — fail closed (no system audio) rather than
                // silently substituting full-system loopback, which the user didn't ask for.
                _system = null;
            }
            catch (Exception)
            {
                // Loopback device unavailable/exclusive-mode conflict — continue without system audio.
                _system = null;
            }
        }

        if (captureMic)
        {
            try
            {
                var mic = new WasapiCapture(); // default capture device, shared mode
                _mic = new MixSource(mic);
                _mic.Start();
            }
            catch (Exception)
            {
                // No mic / unavailable — continue with system only.
                _mic = null;
            }
        }

        IsRunning = _system is not null || _mic is not null;

        return new AudioMixerStartResult
        {
            SystemRequested = captureSystem,
            SystemStarted = _system is not null,
            MicRequested = captureMic,
            MicStarted = _mic is not null,
        };
    }

    public long PumpUntil(NamedPipeServerStream pipe, Func<TimeSpan> activeElapsed, CancellationToken token)
    {
        const int chunkFloats = 4096; // interleaved stereo floats
        float[] sysBuf = new float[chunkFloats];
        float[] micBuf = new float[chunkFloats];
        float[] mixBuf = new float[chunkFloats];
        byte[] outBytes = new byte[chunkFloats * 4];

        long floatsWritten = 0;

        while (!token.IsCancellationRequested)
        {
            double elapsed = activeElapsed().TotalSeconds;
            long targetFloats = (long)(elapsed * Rate * Chans);
            targetFloats -= targetFloats % Chans; // keep stereo-aligned

            while (floatsWritten < targetFloats)
            {
                int n = (int)Math.Min(targetFloats - floatsWritten, chunkFloats);
                Mix(sysBuf, micBuf, mixBuf, n);
                Buffer.BlockCopy(mixBuf, 0, outBytes, 0, n * 4);
                pipe.Write(outBytes, 0, n * 4);
                floatsWritten += n;
            }

            Thread.Sleep(5);
        }

        return floatsWritten * 4;
    }

    private void Mix(float[] sysBuf, float[] micBuf, float[] mixBuf, int n)
    {
        Array.Clear(sysBuf, 0, n);
        Array.Clear(micBuf, 0, n);
        _system?.ReadMixed(sysBuf, n);
        _mic?.ReadMixed(micBuf, n);

        bool sys = _system is { Muted: false };
        bool mic = _mic is { Muted: false };
        float sysGain = _system?.Gain ?? 0f;
        float micGain = _mic?.Gain ?? 0f;

        for (int i = 0; i < n; i++)
        {
            float s = 0f;
            if (sys) s += sysBuf[i] * sysGain;
            if (mic) s += micBuf[i] * micGain;
            mixBuf[i] = AudioMath.SoftClip(s); // soft-clip sum
        }
    }

    public void Stop()
    {
        IsRunning = false;
        _system?.Dispose();
        _mic?.Dispose();
        _system = null;
        _mic = null;
    }

    public void Dispose() => Stop();
}
