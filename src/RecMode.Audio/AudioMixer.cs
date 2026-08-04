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
    private const int ChunkFloats = 4096; // interleaved stereo floats — shared by PumpUntil's buffers and Mix's scratch buffer

    // "System audio" can be more than one physical device (plan: multi-device loopback) — every entry gets
    // the same Gain/Muted (there is exactly one logical System-audio toggle/volume in the UI), summed
    // together in Mix(). Almost always 0 or 1 entries in practice; occasionally 2 (default auto-detect: the
    // Console-role device plus a differently-configured Communications-role device); user-selected custom
    // device lists can be any size.
    private readonly List<MixSource> _systemSources = [];
    private readonly float[] _systemScratch = new float[ChunkFloats];
    private MixSource? _mic;
    private bool _meteringOnly;

    // Guards _systemSources/_mic against the pump thread's concurrent Mix() reads: SetMicEnabled can now
    // open/dispose a MixSource while PumpUntil is actively pulling audio on its own dedicated thread
    // (previously the only mutations were Start()/Stop(), at the very start/end of a recording's audio pump).
    private readonly object _sourceLock = new();

    public bool IsRunning { get; private set; }
    public int SampleRate => Rate;
    public int Channels => Chans;

    public bool SystemEnabled => _systemSources.Count > 0;
    public bool MicEnabled => _mic is not null;

    public float SystemGain
    {
        get => _systemSources.Count > 0 ? _systemSources[0].Gain : 1f;
        set { foreach (MixSource s in _systemSources) s.Gain = value; }
    }

    public bool SystemMuted
    {
        get => _systemSources.Count == 0 || _systemSources[0].Muted;
        set { foreach (MixSource s in _systemSources) s.Muted = value; }
    }

    public float MicGain { get => _mic?.Gain ?? 1f; set { if (_mic is not null) _mic.Gain = value; } }
    public bool MicMuted { get => _mic?.Muted ?? true; set { if (_mic is not null) _mic.Muted = value; } }

    public AudioLevel SystemLevel
    {
        get
        {
            if (_systemSources.Count == 0)
            {
                return AudioLevel.Silent;
            }

            float rms = 0f, peak = 0f;
            foreach (MixSource s in _systemSources)
            {
                AudioLevel level = s.Level;
                rms = Math.Max(rms, level.Rms);
                peak = Math.Max(peak, level.Peak);
            }

            return new AudioLevel(rms, peak);
        }
    }

    public AudioLevel MicLevel => _mic?.Level ?? AudioLevel.Silent;

    public bool SystemFaulted => _systemSources.Count > 0 && _systemSources.Exists(s => s.Faulted);
    public bool MicFaulted => _mic?.Faulted ?? false;

    public AudioMixerStartResult Start(bool captureSystem, bool captureMic, int? targetProcessId = null,
        bool meteringOnly = false, IReadOnlyList<string>? systemDeviceIds = null)
    {
        if (IsRunning)
        {
            Stop();
        }

        _meteringOnly = meteringOnly;

        if (captureSystem)
        {
            if (targetProcessId is int pid)
            {
                StartProcessLoopback(pid, meteringOnly);
            }
            else if (systemDeviceIds is { Count: > 0 })
            {
                // Explicit device selection (Settings → "Which audio devices to record"): capture exactly
                // the chosen devices, however many, instead of the auto-detect below.
                StartCustomDeviceLoopback(systemDeviceIds, meteringOnly);
            }
            else
            {
                // Auto-detect (default): the Console-role default device, plus the Communications-role
                // default device if a user (or Windows) has pointed the two at different physical outputs —
                // see TryStartCommsLoopback's doc comment for why that second device matters (Teams/Zoom/Skype
                // call audio specifically).
                StartConsoleLoopback(meteringOnly);
                StartCommsLoopbackIfDifferent(meteringOnly);
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

        IsRunning = _systemSources.Count > 0 || _mic is not null;

        return new AudioMixerStartResult
        {
            SystemRequested = captureSystem,
            SystemStarted = _systemSources.Count > 0,
            MicRequested = captureMic,
            MicStarted = _mic is not null,
        };
    }

    private void StartProcessLoopback(int pid, bool meteringOnly)
    {
        IWaveIn? capture = null;
        MixSource? system = null;
        try
        {
            capture = new ProcessLoopback.ProcessLoopbackCapture(pid);
            system = new MixSource(capture, meteringOnly);
            system.Start();
            _systemSources.Add(system);
        }
        catch (Exception ex)
        {
            // Target process gone/activation failed — fail closed (no system audio) rather than
            // silently substituting full-system loopback, which the user didn't ask for.
            Log.Warning(ex, "Per-app audio loopback failed for PID {Pid}; system audio disabled for this recording", pid);
            DisposeFailedSource(system, capture);
        }
    }

    private void StartConsoleLoopback(bool meteringOnly)
    {
        IWaveIn? capture = null;
        MixSource? system = null;
        try
        {
            capture = new WasapiLoopbackCapture();
            system = new MixSource(capture, meteringOnly);
            system.Start();
            _systemSources.Add(system);
        }
        catch (Exception ex)
        {
            // Loopback device unavailable/exclusive-mode conflict — continue without system audio.
            Log.Warning(ex, "System-audio loopback capture failed to start; continuing without system audio");
            DisposeFailedSource(system, capture);
        }
    }

    /// <summary>Starts a second system-audio loopback on the default *Communications-role* render device, if
    /// it differs from the Console-role device <see cref="StartConsoleLoopback"/> already covers. Windows lets
    /// a user assign a different physical output to the "Default Communication Device" than the regular
    /// default playback device (Sound settings → App volume & device preferences, or the classic Sound
    /// Control Panel "Communications" tab) — and VoIP apps like Teams/Zoom/Skype render call audio there, not
    /// to the Console-role device most apps use. Looping back only the Console-role default (what a bare
    /// <c>WasapiLoopbackCapture()</c> does) silently misses that app's audio while every other app keeps
    /// recording fine — exactly the "Teams audio missing, everything else fine" report this covers.
    /// Best-effort: any failure (no such role configured, device unavailable) just leaves Communications-role
    /// audio uncaptured, same degrade-gracefully behavior as the primary system source.</summary>
    private void StartCommsLoopbackIfDifferent(bool meteringOnly)
    {
        IWaveIn? capture = null;
        MixSource? comms = null;
        MMDeviceEnumerator? enumerator = null;
        MMDevice? consoleDevice = null;
        MMDevice? commsDevice = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            consoleDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            commsDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            if (string.Equals(consoleDevice.ID, commsDevice.ID, StringComparison.Ordinal))
            {
                // Same physical device — already fully covered by the console-role source, nothing more to capture.
                return;
            }

            // WasapiCapture takes ownership of the MMDevice it's given (disposed alongside the audio client
            // in its own Dispose()); only the console-role handle, which nothing else takes ownership of, is
            // ours to release here.
            capture = new WasapiLoopbackCapture(commsDevice);
            comms = new MixSource(capture, meteringOnly);
            comms.Start();
            _systemSources.Add(comms);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Communications-role loopback capture unavailable; system audio will only cover the Console-role default device");
            DisposeFailedSource(comms, capture);
        }
        finally
        {
            consoleDevice?.Dispose();
            enumerator?.Dispose();
        }
    }

    /// <summary>Opens one loopback capture per explicitly-selected device ID (Settings → audio device picker).
    /// Each device is independent: one failing (unplugged since the picker was last opened, etc.) is logged
    /// and skipped rather than aborting the whole selection — the recording still gets audio from whichever
    /// selected devices are actually available.</summary>
    private void StartCustomDeviceLoopback(IReadOnlyList<string> deviceIds, bool meteringOnly)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (string id in deviceIds)
        {
            IWaveIn? capture = null;
            MixSource? source = null;
            MMDevice? device = null;
            try
            {
                device = enumerator.GetDevice(id);
                capture = new WasapiLoopbackCapture(device); // capture owns and disposes `device`
                source = new MixSource(capture, meteringOnly);
                source.Start();
                _systemSources.Add(source);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Selected system-audio device {DeviceId} is unavailable; skipping it for this recording", id);
                DisposeFailedSource(source, capture);
                if (source is null)
                {
                    device?.Dispose();
                }
            }
        }
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

    /// <summary>Live-toggles the microphone source on an already-running mixer, so a mid-recording
    /// <c>MicEnabled</c> change (Record screen checkbox/compact-launcher toggle) actually takes effect on the
    /// recording in progress instead of only the next one — direct user request: forgetting to enable the mic
    /// before hitting Record (or deciding to turn it off partway through) shouldn't mean losing that audio for
    /// the whole file. No-op (returns the current state) if the mixer isn't running or already matches the
    /// requested state. Returns whether mic capture ended up enabled — false on a genuine device failure
    /// (unplugged, exclusive-mode conflict), same as <see cref="Start"/>'s own degraded-source handling; the
    /// caller (<see cref="RecMode.App.Services.Recording.RecordingCoordinator.SetMicEnabled"/>) surfaces that
    /// as a warning.</summary>
    public bool SetMicEnabled(bool enabled)
    {
        if (!IsRunning)
        {
            return false;
        }

        lock (_sourceLock)
        {
            bool currentlyEnabled = _mic is not null;
            if (enabled == currentlyEnabled)
            {
                return currentlyEnabled;
            }

            if (!enabled)
            {
                _mic?.Dispose();
                _mic = null;
                return false;
            }

            IWaveIn? capture = null;
            MixSource? micSource = null;
            try
            {
                capture = new WasapiCapture(); // default capture device, shared mode — mirrors Start()'s own mic path
                micSource = new MixSource(capture, _meteringOnly);
                micSource.Start();
                _mic = micSource;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Microphone capture failed to start (live toggle); continuing without microphone audio");
                DisposeFailedSource(micSource, capture);
                _mic = null;
                return false;
            }
        }
    }

    public void ClearBuffers()
    {
        foreach (MixSource s in _systemSources) s.ClearBuffer();
        _mic?.ClearBuffer();
    }

    public long PumpUntil(NamedPipeServerStream pipe, Func<TimeSpan> segmentElapsed, CancellationToken token, int offsetMs = 0)
    {
        float[] sysBuf = new float[ChunkFloats];
        float[] micBuf = new float[ChunkFloats];
        float[] mixBuf = new float[ChunkFloats];
        byte[] outBytes = new byte[ChunkFloats * 4];

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
                int n = (int)Math.Min(silenceRemaining, ChunkFloats);
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
                int n = (int)Math.Min(targetFloats - floatsWritten, ChunkFloats);
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

        lock (_sourceLock)
        {
            // Keep source ownership pinned while reading. SetMicEnabled/Stop dispose under the same
            // lock; taking a reference and releasing it before ReadMixed allowed disposal to race the
            // audio pump and invoke WASAPI on an already-disposed source.
            foreach (MixSource source in _systemSources)
            {
                // Always drain (even muted) so a source's buffer doesn't silently overflow-and-discard while
                // muted, only to hand back a backlog of stale audio the instant it's unmuted mid-recording.
                source.ReadMixed(_systemScratch, n);
                if (source.Muted)
                {
                    continue;
                }

                for (int i = 0; i < n; i++) sysBuf[i] += _systemScratch[i] * source.Gain;
            }

            _mic?.ReadMixed(micBuf, n);
            bool micOn = _mic is { Muted: false };
            float micGain = _mic?.Gain ?? 0f;

            for (int i = 0; i < n; i++)
            {
                float s = sysBuf[i];
                if (micOn) s += micBuf[i] * micGain;
                mixBuf[i] = AudioMath.SoftClip(s); // soft-clip sum
            }
        }
    }

    public void Stop()
    {
        IsRunning = false;
        lock (_sourceLock)
        {
            foreach (MixSource s in _systemSources) s.Dispose();
            _systemSources.Clear();
            _mic?.Dispose();
            _mic = null;
        }
    }

    public void Dispose() => Stop();
}
