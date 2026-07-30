using System.IO.Pipes;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

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

    public bool SystemFaulted => _system?.Faulted ?? false;
    public bool MicFaulted => _mic?.Faulted ?? false;

    public AudioMixerStartResult Start(bool captureSystem, bool captureMic, int? targetProcessId = null, bool meteringOnly = false)
    {
        if (IsRunning)
        {
            Stop();
        }

        if (captureSystem)
        {
            // `capture` is tracked separately from `system`: the MixSource constructor reads
            // capture.WaveFormat, which lazily queries the device's mix format and can genuinely throw — at
            // which point `system` is still null, so disposing only via `system` left the IWaveIn (holding a
            // live IAudioClient/MMDevice) undisposed. Same shape for the mic below.
            IWaveIn? capture = null;
            MixSource? system = null;
            try
            {
                capture = targetProcessId is int pid
                    ? new ProcessLoopback.ProcessLoopbackCapture(pid)
                    : new WasapiLoopbackCapture();
                system = new MixSource(capture, meteringOnly);
                system.Start();
                _system = system;
            }
            catch (Exception ex) when (targetProcessId is not null)
            {
                // Target process gone/activation failed — fail closed (no system audio) rather than
                // silently substituting full-system loopback, which the user didn't ask for.
                Log.Warning(ex, "Per-app audio loopback failed for PID {Pid}; system audio disabled for this recording", targetProcessId);
                DisposeFailedSource(system, capture);
                _system = null;
            }
            catch (Exception ex)
            {
                // Loopback device unavailable/exclusive-mode conflict — continue without system audio.
                Log.Warning(ex, "System-audio loopback capture failed to start; continuing without system audio");
                DisposeFailedSource(system, capture);
                _system = null;
            }
        }

        if (captureMic)
        {
            IWaveIn? capture = null;
            MixSource? micSource = null;
            try
            {
                capture = new WasapiCapture(); // default capture device, shared mode
                micSource = new MixSource(capture, meteringOnly);
                micSource.Start();
                _mic = micSource;
            }
            catch (Exception ex)
            {
                // No mic / unavailable — continue with system only.
                Log.Warning(ex, "Microphone capture failed to start; continuing without microphone audio");
                DisposeFailedSource(micSource, capture);
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

    /// <summary>Cleans up a half-constructed source. When <paramref name="source"/> exists it owns (and
    /// disposes) the capture; when construction failed before that, the capture has to be disposed directly
    /// or its IAudioClient/MMDevice leaks — which on the mic path leaves Windows' "microphone in use"
    /// indicator lit for the rest of the process.</summary>
    private static void DisposeFailedSource(MixSource? source, IWaveIn? capture)
    {
        if (source is not null)
        {
            source.Dispose();
            return;
        }

        try
        {
            capture?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Disposing a failed audio capture threw; ignoring");
        }
    }

    public void ClearBuffers()
    {
        _system?.ClearBuffer();
        _mic?.ClearBuffer();
    }

    public long PumpUntil(NamedPipeServerStream pipe, Func<TimeSpan> segmentElapsed, CancellationToken token, int offsetMs = 0)
    {
        const int chunkFloats = 4096; // interleaved stereo floats
        float[] sysBuf = new float[chunkFloats];
        float[] micBuf = new float[chunkFloats];
        float[] mixBuf = new float[chunkFloats];
        byte[] outBytes = new byte[chunkFloats * 4];

        long floatsWritten = 0;

        // A/V sync offset, applied as real samples at the front of the stream (see IAudioMixer.PumpUntil).
        // Positive: prepend silence, pushing every real sample that much later relative to video. Negative:
        // discard that much leading audio, pulling the rest earlier. Computed once, up front — applying a
        // shift mid-stream would be an audible discontinuity, not a sync correction.
        (long silenceRemaining, long discardRemaining) = AudioSyncOffset.ComputePlan(offsetMs, Rate, Chans);
        long silencePrepended = silenceRemaining;

        if (silenceRemaining > 0)
        {
            Array.Clear(outBytes);
            while (silenceRemaining > 0 && !token.IsCancellationRequested)
            {
                int n = (int)Math.Min(silenceRemaining, chunkFloats);
                pipe.WriteAsync(outBytes.AsMemory(0, n * 4), token).AsTask().GetAwaiter().GetResult();
                silenceRemaining -= n;
                floatsWritten += n;
            }
        }

        while (!token.IsCancellationRequested)
        {
            double elapsed = Math.Max(0, segmentElapsed().TotalSeconds);
            // The prepended silence counts toward what's already been written, so the elapsed-driven target
            // has to include it — otherwise the pump would think it was that far ahead and stall until real
            // time caught up, re-introducing exactly the desync this is correcting.
            long targetFloats = (long)(elapsed * Rate * Chans) + silencePrepended;
            targetFloats -= targetFloats % Chans; // keep stereo-aligned

            while (floatsWritten < targetFloats)
            {
                int n = (int)Math.Min(targetFloats - floatsWritten, chunkFloats);
                Mix(sysBuf, micBuf, mixBuf, n);

                long dropped = 0;
                if (discardRemaining > 0)
                {
                    // Mixed (so the capture buffers stay drained in lockstep with the clock) but not written:
                    // this is the leading audio being dropped to pull the rest earlier.
                    dropped = Math.Min(discardRemaining, n);
                    discardRemaining -= dropped;
                    floatsWritten += dropped;
                    if (dropped == n)
                    {
                        continue;
                    }
                    n -= (int)dropped;
                }

                // Copy from mixBuf[dropped..], not mixBuf[0..]: Mix() above always fills mixBuf starting at
                // index 0 for the full original chunk length, so once a partial discard trims the leading
                // `dropped` samples off, the samples actually being KEPT start at that offset, not at 0.
                // Copying from 0 wrote the very samples meant to be dropped and silently lost the tail of the
                // chunk instead — a splice discontinuity (an audible click) a fraction of a second into every
                // recording made with a negative AudioSyncOffsetMs, on exactly the one chunk where the discard
                // ends partway through.
                Buffer.BlockCopy(mixBuf, (int)dropped * 4, outBytes, 0, n * 4);
                pipe.WriteAsync(outBytes.AsMemory(0, n * 4), token).AsTask().GetAwaiter().GetResult();
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
