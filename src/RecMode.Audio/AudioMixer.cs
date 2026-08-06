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
    private const int ChunkFloats = 4096; // interleaved stereo floats — shared by PumpUntil's buffers and Mix's scratch buffer

    // "System audio" can be more than one physical device (plan: multi-device loopback) — every entry gets
    // the same Gain/Muted (there is exactly one logical System-audio toggle/volume in the UI), summed
    // together in Mix(). Almost always 0 or 1 entries in practice; occasionally 2 (default auto-detect: the
    // Console-role device plus a differently-configured Communications-role device); user-selected custom
    // device lists can be any size.
    //
    // COPY-ON-WRITE, deliberately. Readers (Mix on the audio-pump thread; SystemGain/SystemMuted/SystemLevel/
    // SystemFaulted/SystemEnabled/ClearBuffers from the UI thread) just grab the array reference — a single
    // atomic read — and iterate their own immutable snapshot, with no lock and no TOCTOU. Writers replace the
    // array wholesale under _writeLock.
    //
    // The previous design took one lock around every access instead. That closed the TOCTOU hole but opened a
    // priority inversion: Stop() holds the lock across MixSource.Dispose, which joins WASAPI capture threads
    // (~50 ms each, and up to 2 s for per-app process loopback's two 1 s joins) — so the 30 Hz meter tick,
    // which runs on the UI thread and reads SystemLevel, could block the entire window for that whole
    // teardown, right when the user hits Stop. Disposal now happens strictly OUTSIDE the lock, on a detached
    // snapshot, so no reader can ever wait on a blocking COM call.
    private volatile MixSource[] _systemSources = [];
    private readonly float[] _systemScratch = new float[ChunkFloats];
    private volatile MixSource? _mic;
    private bool _meteringOnly;

    /// <summary>Serializes writers only (Start/Stop/SetMicEnabled). Readers are lock-free — see _systemSources.
    /// Never hold this across a blocking WASAPI/COM call; detach under the lock, dispose after releasing it.</summary>
    private readonly object _writeLock = new();

    public bool IsRunning { get; private set; }
    public int SampleRate => Rate;
    public int Channels => Chans;

    public bool SystemEnabled => _systemSources.Length > 0;
    public bool MicEnabled => _mic is not null;

    public float SystemGain
    {
        get { MixSource[] s = _systemSources; return s.Length > 0 ? s[0].Gain : 1f; }
        set { foreach (MixSource s in _systemSources) s.Gain = value; }
    }

    public bool SystemMuted
    {
        get { MixSource[] s = _systemSources; return s.Length == 0 || s[0].Muted; }
        set { foreach (MixSource s in _systemSources) s.Muted = value; }
    }

    public float MicGain { get => _mic?.Gain ?? 1f; set { if (_mic is { } m) m.Gain = value; } }
    public bool MicMuted { get => _mic?.Muted ?? true; set { if (_mic is { } m) m.Muted = value; } }

    /// <summary>
    /// Combined level across every system source. Sums rather than taking the max, because <see cref="Mix"/>
    /// SUMS the sources — a max would under-report whenever more than one device is active, so a mix already
    /// being limited by <see cref="AudioMath.SoftClip"/> could still show a comfortable level and never turn
    /// the meter's caution colour on. RMS adds in power terms (sqrt of summed squares, correct for
    /// uncorrelated sources); peak adds directly, the worst case, which is the right bias for a clipping
    /// indicator. Both reduce to exactly the single source's own value in the overwhelmingly common 1-device
    /// case, so this changes nothing for typical users. Pre-gain, matching the meter's long-standing semantics.
    /// </summary>
    public AudioLevel SystemLevel
    {
        get
        {
            MixSource[] sources = _systemSources;
            if (sources.Length == 0)
            {
                return AudioLevel.Silent;
            }

            float sumSquares = 0f, sumPeak = 0f;
            foreach (MixSource s in sources)
            {
                AudioLevel level = s.Level; // already Silent while muted
                sumSquares += level.Rms * level.Rms;
                sumPeak += level.Peak;
            }

            return new AudioLevel(Math.Min(1f, MathF.Sqrt(sumSquares)), Math.Min(1f, sumPeak));
        }
    }

    public AudioLevel MicLevel => _mic?.Level ?? AudioLevel.Silent;

    public bool SystemFaulted { get { foreach (MixSource s in _systemSources) { if (s.Faulted) return true; } return false; } }
    public bool MicFaulted => _mic?.Faulted ?? false;

    public AudioMixerStartResult Start(bool captureSystem, bool captureMic, int? targetProcessId = null,
        bool meteringOnly = false, IReadOnlyList<string>? systemDeviceIds = null, bool captureCommsRoleAudio = false)
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
            else if (systemDeviceIds is not null)
            {
                // Explicit device selection (Record screen → "Select audio devices…"): capture exactly the
                // chosen devices, however many, instead of the auto-detect below.
                //
                // Tested for null, NOT for Count > 0. An EMPTY list is a meaningful, deliberate choice — the
                // picker's own Result contract documents unchecking everything as "record no system audio" —
                // and StartCustomDeviceLoopback no-ops harmlessly on it, leaving zero sources. Testing
                // Count > 0 instead sent that case down the auto-detect branch below, so a user who
                // explicitly excluded every device still got the default playback device (plus the
                // Communications one) recorded, silently, on that and every subsequent recording.
                StartCustomDeviceLoopback(systemDeviceIds, meteringOnly);
            }
            else
            {
                // Auto-detect (default): the Console-role default device, plus the Communications-role
                // default device if a user (or Windows) has pointed the two at different physical outputs —
                // see TryStartCommsLoopback's doc comment for why that second device matters (Teams/Zoom/Skype
                // call audio specifically).
                StartConsoleLoopback(meteringOnly);
                if (captureCommsRoleAudio)
                {
                    StartCommsLoopbackIfDifferent(meteringOnly);
                }
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

        bool systemStarted = _systemSources.Length > 0;
        IsRunning = systemStarted || _mic is not null;

        return new AudioMixerStartResult
        {
            // An explicitly-empty device selection means the user asked for zero system devices, so report it
            // as "not requested" rather than letting SystemDegraded fire a "couldn't capture system audio"
            // warning for something they deliberately chose.
            SystemRequested = captureSystem && systemDeviceIds is not { Count: 0 },
            SystemStarted = systemStarted,
            MicRequested = captureMic,
            MicStarted = _mic is not null,
        };
    }

    /// <summary>Appends one system source, copy-on-write (see <c>_systemSources</c>). Writers are rare
    /// (recording/metering start), readers are hot — so the allocation per add is the right trade for
    /// lock-free, TOCTOU-free reads on the audio-pump and UI threads.</summary>
    private void AddSystemSource(MixSource source)
    {
        lock (_writeLock)
        {
            _systemSources = [.. _systemSources, source];
        }
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
            AddSystemSource(system);
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
            AddSystemSource(system);
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
            // in its own Dispose()), so once this succeeds commsDevice is no longer ours to release.
            capture = new WasapiLoopbackCapture(commsDevice);
            comms = new MixSource(capture, meteringOnly);
            comms.Start();
            AddSystemSource(comms);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Communications-role loopback capture unavailable; system audio will only cover the Console-role default device");
            DisposeFailedSource(comms, capture);
        }
        finally
        {
            consoleDevice?.Dispose();
            // Only the capture takes ownership of commsDevice — so release it on every path where no capture
            // was actually constructed. That includes the *most common* one: the early return when Console and
            // Communications resolve to the same device, which is the default Windows configuration. It used
            // to be dropped there on every single Start(), i.e. every recording and every nav to the Record screen.
            if (capture is null)
            {
                commsDevice?.Dispose();
            }

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
                AddSystemSource(source);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Selected system-audio device {DeviceId} is unavailable; skipping it for this recording", id);
                DisposeFailedSource(source, capture);
                // Ownership passes to the capture, so key the cleanup on whether a capture was actually
                // constructed — not on `source is null`, which wrongly left `device` undisposed whenever the
                // capture succeeded but MixSource construction or Start() then threw.
                if (capture is null)
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

        // Only the state swap is serialized. Device activation (new WasapiCapture() -> MMDeviceEnumerator COM
        // round-trip -> IAudioClient init) and teardown (which joins the WASAPI capture thread) both happen
        // OUTSIDE the lock, so a mid-recording mic toggle can never stall the audio pump or the UI meter tick.
        MixSource? toDispose = null;
        try
        {
            lock (_writeLock)
            {
                bool currentlyEnabled = _mic is not null;
                if (enabled == currentlyEnabled)
                {
                    return currentlyEnabled;
                }

                if (!enabled)
                {
                    toDispose = _mic;
                    _mic = null;
                    IsRunning = _systemSources.Length > 0;
                    return false;
                }
            }

            IWaveIn? capture = null;
            MixSource? micSource = null;
            try
            {
                capture = new WasapiCapture(); // default capture device, shared mode — mirrors Start()'s own mic path
                micSource = new MixSource(capture, _meteringOnly);
                micSource.Start();
                lock (_writeLock)
                {
                    _mic = micSource;
                    IsRunning = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Microphone capture failed to start (live toggle); continuing without microphone audio");
                DisposeFailedSource(micSource, capture);
                lock (_writeLock)
                {
                    _mic = null;
                    IsRunning = _systemSources.Length > 0;
                }

                return false;
            }
        }
        finally
        {
            SafeDispose(toDispose);
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

        // Lock-free: one atomic read of the copy-on-write array gives an immutable snapshot. MixSource.Dispose
        // deliberately leaves the buffer/sample-provider chain intact (see its doc comment), so even if a
        // source in this snapshot is being disposed concurrently, reading from it is safe and simply yields
        // silence.
        foreach (MixSource source in _systemSources)
        {
            // Always drain (even muted) so a source's buffer doesn't silently overflow-and-discard while
            // muted, only to hand back a backlog of stale audio the instant it's unmuted mid-recording.
            //
            // Bound the accumulate by what was ACTUALLY read: _systemScratch is shared across sources and
            // never cleared, so on a short read (WdlResamplingSampleProvider genuinely returns fewer frames
            // than asked during filter warm-up — in play for any source whose native rate isn't 48 kHz) the
            // tail still holds the PREVIOUS source's samples. Summing the full n re-mixed another device's
            // audio in at full gain. The pre-multi-device code read straight into a freshly-cleared sysBuf,
            // so a short read correctly left silence; the shared scratch buffer lost that property.
            int read = source.ReadMixed(_systemScratch, n);
            if (source.Muted)
            {
                continue;
            }

            float gain = source.Gain;
            for (int i = 0; i < read; i++) sysBuf[i] += _systemScratch[i] * gain;
        }

        MixSource? mic = _mic;
        int micRead = mic?.ReadMixed(micBuf, n) ?? 0;
        bool micOn = mic is { Muted: false };
        float micGain = mic?.Gain ?? 0f;

        for (int i = 0; i < n; i++)
        {
            float s = sysBuf[i];
            if (micOn && i < micRead) s += micBuf[i] * micGain;
            mixBuf[i] = AudioMath.SoftClip(s); // soft-clip sum
        }
    }

    public void Stop()
    {
        IsRunning = false;

        // Detach under the lock, dispose after releasing it — MixSource.Dispose joins WASAPI capture threads
        // (up to ~2 s for per-app process loopback), and holding the lock across that would block the UI
        // thread's meter tick for the whole teardown.
        MixSource[] systemToDispose;
        MixSource? micToDispose;
        lock (_writeLock)
        {
            systemToDispose = _systemSources;
            micToDispose = _mic;
            _systemSources = [];
            _mic = null;
        }

        foreach (MixSource s in systemToDispose) SafeDispose(s);
        SafeDispose(micToDispose);
    }

    /// <summary>Disposes one source, isolating failures. Without this, a single device throwing on teardown
    /// (a COM failure from an endpoint removed mid-recording) skipped disposal of every remaining source and
    /// propagated out of <see cref="Stop"/> into <c>RecordingCoordinator.Finalize</c>, which is not itself
    /// guarded — so it also skipped the safe-recording MKV→MP4 remux and the library-index write, leaving a
    /// perfectly good recording stranded as a <c>.recording.mkv</c> with no Library entry.</summary>
    private static void SafeDispose(MixSource? source)
    {
        try
        {
            source?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Disposing an audio source threw; continuing teardown");
        }
    }

    public void Dispose() => Stop();
}
