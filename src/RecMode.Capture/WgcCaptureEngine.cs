using System.Diagnostics;
using RecMode.Capture.Webcam;
using Serilog;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;

namespace RecMode.Capture;

/// <summary>
/// Default <see cref="ICaptureEngine"/>: Windows.Graphics.Capture on the discrete GPU, converting each
/// captured BGRA frame to NV12 and publishing it as "latest" for the CFR pacer to pull. Event-driven
/// (WGC <c>FrameArrived</c>), no polling (plan §3.9). Not thread-safe across Start/Stop; call from one thread.
/// </summary>
public sealed class WgcCaptureEngine : ICaptureEngine
{
    private readonly Lock _sync = new();
    private readonly Lock _stopLock = new();
    private readonly Lock _disposeGuard = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private Nv12Converter? _converter;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private DesktopDuplicationCaptureSource? _ddaSource;
    private Thread? _ddaThread;
    private volatile bool _ddaStopping;
    private readonly ManualResetEventSlim _ddaThreadExited = new(initialState: false);
    private byte[] _latest = [];
    private byte[] _scratch = [];
    private bool _hasLatest;
    private long _capturedFrames;

    // Mirrors GdiCaptureEngine.MaxConsecutiveFrameFailures — see OnFrameArrived for why the WGC path needs
    // the same tolerance now that a capture fault ends the recording outright.
    private const int MaxConsecutiveFrameFailures = 30;
    private int _consecutiveFrameFailures;
    private readonly FrameRateLimiter _rateLimiter = new(Stopwatch.Frequency);

    /// <summary>Passive video-path latency measurement (see <see cref="CaptureLatencyTracker"/>). Diagnostic
    /// only — logged, never acted on. 10s windows keep it to ~6 log lines a minute during a recording, which
    /// is enough resolution to see drift over a long capture without flooding the log.</summary>
    private readonly CaptureLatencyTracker _latencyTracker = new(TimeSpan.FromSeconds(10));
    private IWebcamFrameSource? _webcamSource;
    private RegionRect? _webcamRect;
    private double _brightness;
    private GdiCaptureEngine? _softwareFallback;

    public bool IsRunning { get; private set; }
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }
    public int Nv12ByteSize { get; private set; }
    // MUST delegate to the software fallback, like TryGetLatestFrame and every setter below: _capturedFrames
    // is only ever incremented by OnFrameArrivedCore (the WGC path), so on the GDI fallback it stays 0
    // forever. RecordingCoordinator's stale-capture watchdog polls exactly this property and force-stops a
    // recording after 10s of no increase — so without this delegation every recording on the fallback path
    // (Win10 pre-1903, RDP/VM sessions, any WGC device-creation failure) self-terminated after ~11 seconds.
    public long CapturedFrameCount => _softwareFallback?.CapturedFrameCount ?? Interlocked.Read(ref _capturedFrames);
    public bool SupportsZoom => _softwareFallback is null;

    /// <summary>True once HDR-to-SDR tone mapping (§3.6) is actually active for the current recording — only
    /// ever true for Monitor/Region sources on an HDR-active display; Window and All-Displays sources don't
    /// attempt it in this first cut (documented scope cut, same precedent as Smart auto-zoom's Window/
    /// All-Displays exclusion — mapping either reliably onto one monitor's HDR state needs more plumbing than
    /// a first pass warrants).</summary>
    public bool HdrToneMapActive => _converter?.HdrToneMapActive ?? false;

    public event EventHandler<Exception>? Faulted;

    public void Start(CaptureTarget target, int dstW, int dstH, bool captureCursor, int targetFps = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRunning)
        {
            throw new InvalidOperationException("Capture is already running.");
        }

        _rateLimiter.SetTargetFps(targetFps);

        if (!CaptureCapabilities.IsSupported())
        {
            StartSoftwareFallback(target, dstW, dstH, captureCursor);
            return;
        }

        if (target.Kind == CaptureKind.AllDisplays)
        {
            try { StartAllDisplays(dstW, dstH); }
            catch (Exception ex)
            {
                Log.Warning(ex, "All-Displays desktop-duplication capture failed to start; falling back to GDI");
                StartSoftwareFallback(target, dstW, dstH, captureCursor);
            }
            return;
        }

        // HDR-to-SDR tone mapping (§3.6): only attempted for Monitor/Region sources, where the target maps
        // 1:1 onto one real monitor's HDR state — Window and All-Displays sources are a deliberate scope cut
        // (same precedent as Smart auto-zoom's Window/All-Displays exclusion).
        bool sourceIsHdr = target.Kind is CaptureKind.Monitor or CaptureKind.Region &&
            CaptureCapabilities.EnumerateMonitors().FirstOrDefault(m => m.Handle == target.Handle) is { IsHdr: true };

        WgcSessionFactory.Session session;
        try { session = WgcSessionFactory.Start(target, captureCursor, OnFrameArrived, sourceIsHdr); }
        catch (Exception ex)
        {
            // The one place this sandbox's own standing DXGI_ERROR_UNSUPPORTED gap (see CLAUDE.md) actually
            // surfaces — without this log line, that fallback happens completely invisibly.
            Log.Warning(ex, "Windows.Graphics.Capture session failed to start; falling back to GDI");
            StartSoftwareFallback(target, dstW, dstH, captureCursor);
            return;
        }
        try
        {
            _device = session.Device;
            _context = session.Context;
            _framePool = session.FramePool;
            _session = session.CaptureSession;
            _item = session.Item;
            _item.Closed += OnCaptureItemClosed;

            int srcW = session.Item.Size.Width, srcH = session.Item.Size.Height;
            _converter = new Nv12Converter(_device, _context, srcW, srcH, dstW, dstH, target.Region, sourceIsHdr);
            _converter.SetWebcamOverlay(_webcamSource, _webcamRect);
            _converter.SetBrightness(_brightness);
            OutputWidth = dstW;
            OutputHeight = dstH;
            Nv12ByteSize = _converter.Nv12ByteSize;
            _latest = new byte[Nv12ByteSize];
            _scratch = new byte[Nv12ByteSize];
            _hasLatest = false;
            _capturedFrames = 0;
            IsRunning = true;
        }
        catch
        {
            DisposeWgcResources();
            throw;
        }
    }

    private void StartSoftwareFallback(CaptureTarget target, int dstW, int dstH, bool captureCursor)
    {
        _softwareFallback = new GdiCaptureEngine();
        _softwareFallback.Faulted += (s, e) => Faulted?.Invoke(this, e);
        _softwareFallback.Start(target, dstW, dstH, captureCursor);
        OutputWidth = dstW;
        OutputHeight = dstH;
        Nv12ByteSize = _softwareFallback.Nv12ByteSize;
        IsRunning = true;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object? args)
    {
        // This callback is invoked by CsWinRT's native-to-managed delegate wrapper: an exception escaping it
        // is converted into an HRESULT for the (native) caller and never reaches managed code as an unhandled
        // exception — it is silently swallowed, not merely "not our thread." Without this try/catch, a GPU
        // TDR / device-lost error here (e.g. from Convert's Blt+readback) vanished with no log line and no
        // Faulted event, and the CFR pacer kept duplicating the last successfully-converted frame at full fps
        // for the rest of the recording — a full-length file that's silently frozen from that instant on.
        // DdaPumpLoop already guards its own GPU work the same way; this was the one capture path (the
        // default one) that never got it.
        try
        {
            OnFrameArrivedCore(pool);
            Interlocked.Exchange(ref _consecutiveFrameFailures, 0);
        }
        catch (Exception ex)
        {
            // Tolerate a run of transient failures before escalating, matching GdiCaptureEngine's own
            // MaxConsecutiveFrameFailures policy and DesktopDuplicationCaptureSource's re-acquire-on-
            // ACCESS_LOST behaviour. A display-mode change, a secure-desktop/UAC transition, or a brief
            // device-removed-and-restored can throw here once; since OnCaptureFaulted now ENDS the recording
            // rather than just warning, escalating on the first blip would make the default capture path
            // strictly the least fault-tolerant of the three — a single hiccup would kill a long take.
            if (Interlocked.Increment(ref _consecutiveFrameFailures) < MaxConsecutiveFrameFailures)
            {
                Log.Debug(ex, "WGC frame conversion failed (transient, continuing)");
                return;
            }

            Log.Warning(ex, "WGC frame conversion failed {Count} times consecutively; faulting capture",
                MaxConsecutiveFrameFailures);
            try
            {
                Faulted?.Invoke(this, ex);
            }
            catch (Exception)
            {
                // A misbehaving subscriber must not prevent this callback from returning cleanly to WinRT.
            }
        }
    }

    private void OnFrameArrivedCore(Direct3D11CaptureFramePool pool)
    {
        // _disposeGuard (not _sync) wraps the GPU work here — it also doubles as Stop()'s barrier (an empty
        // critical section taken after unsubscribing this callback, before disposing the converter/context/
        // device it uses below): a call already dispatched before the unsubscribe takes effect blocks here
        // until Stop() finishes, then sees _converter already null and returns — instead of racing GPU-object
        // disposal (COM refcount corruption). Using a dedicated lock rather than _sync means the GPU convert
        // (a Blt + staging readback that can take several ms) no longer contends with TryGetLatestFrame's
        // plain memcpy — the two used to share _sync, so a slow encode-frame readback could stall the CFR
        // pacer's read, and vice versa. _sync itself is now held only for the O(1) buffer-reference swap.
        Nv12Converter? converter;
        lock (_disposeGuard)
        {
            converter = _converter;
            if (converter is null)
            {
                return;
            }

            using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            // Throttle the expensive GPU convert + readback to the recording/preview's own target fps — WGC
            // fires FrameArrived on-change, up to the source monitor's own refresh rate (e.g. 144 Hz), but the
            // CFR pacer only ever reads TryGetLatestFrame up to targetFps times/sec, so every conversion above
            // that rate was pure waste (e.g. up to ~84 of every 144 conversions/sec at 1440p60 on a 144 Hz
            // display, immediately overwritten before the pacer ever consumed them). Still calls
            // TryGetNextFrame above unconditionally so the WGC frame pool keeps cycling normally.
            long nowTicks = Stopwatch.GetTimestamp();
            if (!_rateLimiter.ShouldAccept(nowTicks))
            {
                return;
            }

            // Passive video-path latency measurement (diagnostic only — nothing acts on it). Read here,
            // right before the GPU convert, so it captures WGC delivery latency without including our own
            // convert+readback cost, which the pacer's own timing already covers. One property read and a
            // few adds per accepted frame; no allocation, no extra syscall (Stopwatch.GetTimestamp was
            // already called just above by the rate limiter — reused rather than re-read so the two can't
            // disagree). See CaptureLatencyTracker for why this exists and what it can't tell us.
            _latencyTracker.Record(frame.SystemRelativeTime, nowTicks);

            using ID3D11Texture2D tex = CaptureInterop.GetTexture(frame.Surface);
            converter.Convert(tex, _scratch);

            // Swap under the SAME _disposeGuard critical section as the convert above, not a separate _sync
            // block after it — the frame pool is CreateFreeThreaded (WgcSessionFactory), so FrameArrived can
            // be dispatched concurrently, which is exactly why _disposeGuard exists. With the swap outside
            // it, callback A could convert into _scratch and release the lock before swapping; callback B
            // then acquires the lock and converts into the SAME _scratch (A hasn't swapped it away yet),
            // corrupting A's still-pending write, and whichever callback swaps last can publish the OLDER
            // converted frame as "latest" over a newer one. WgcPreviewEngine already does this correctly
            // (swap nested inside its own _disposeGuard) — this brings the recording path in line with it.
            lock (_sync)
            {
                (_scratch, _latest) = (_latest, _scratch);
                _hasLatest = true;
            }

            Interlocked.Increment(ref _capturedFrames);
        }

        ReportLatencyIfDue();
    }

    /// <summary>Logs one window of video-path capture latency, if a window's worth has accumulated. Called
    /// outside the <c>_disposeGuard</c> critical section deliberately — Serilog's file sink can block, and
    /// this lock also serialises the GPU convert for every frame, so logging inside it would put I/O on the
    /// capture hot path.
    /// <para>
    /// Logged at Information (not Debug) on purpose: the entire point is that someone running RecMode on a
    /// real WGC-capable machine — which this dev environment is not — can hand back an ordinary log file and
    /// have the numbers already in it, without needing to reconfigure log levels first.
    /// </para></summary>
    private void ReportLatencyIfDue()
    {
        if (_latencyTracker.TryTakeReport(Stopwatch.GetTimestamp(), out CaptureLatencyStats stats))
        {
            Log.Information("Capture latency (compositor-render to convert): {Stats}", stats);
        }
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object? args)
    {
        Faulted?.Invoke(this, new InvalidOperationException("The captured window or display was closed."));
        ThreadPool.QueueUserWorkItem(_ => Stop());
    }

    private void DisposeWgcResources()
    {
        if (_framePool is not null) _framePool.FrameArrived -= OnFrameArrived;
        if (_item is not null) _item.Closed -= OnCaptureItemClosed;
        _session?.Dispose(); _framePool?.Dispose(); _converter?.Dispose(); _context?.Dispose(); _device?.Dispose();
        _session = null; _framePool = null; _item = null; _converter = null; _context = null; _device = null;
    }

    /// <summary>"All Displays" source: no WGC item exists for this, so <see cref="DesktopDuplicationCaptureSource"/>
    /// composites every monitor via DXGI Desktop Duplication instead, pulled from a dedicated thread (DDA has no
    /// event-driven callback like WGC's <c>FrameArrived</c> — <c>AcquireNextFrame</c> is a blocking wait, not a
    /// busy-poll, so this doesn't run against plan §3.9's "event-driven, never polled" rule).</summary>
    private void StartAllDisplays(int dstW, int dstH)
    {
        IReadOnlyList<MonitorInfo> monitors = CaptureCapabilities.EnumerateMonitors();
        try
        {
            _ddaSource = new DesktopDuplicationCaptureSource(monitors);
            _device = _ddaSource.Device;
            _context = _ddaSource.Context;
            _converter = new Nv12Converter(_device, _context, _ddaSource.VirtualWidth, _ddaSource.VirtualHeight, dstW, dstH);
            _converter.SetWebcamOverlay(_webcamSource, _webcamRect);
            _converter.SetBrightness(_brightness);
            OutputWidth = dstW; OutputHeight = dstH; Nv12ByteSize = _converter.Nv12ByteSize;
            _latest = new byte[Nv12ByteSize]; _scratch = new byte[Nv12ByteSize]; _hasLatest = false; _capturedFrames = 0;
            _ddaStopping = false; _ddaThreadExited.Reset();
            _ddaThread = new Thread(DdaPumpLoop) { IsBackground = true, Name = "recmode-dda" };
            _ddaThread.Start(); IsRunning = true;
        }
        catch
        {
            _converter?.Dispose(); _ddaSource?.Dispose(); _context?.Dispose(); _device?.Dispose();
            _converter = null; _ddaSource = null; _context = null; _device = null;
            throw;
        }
    }

    /// <summary>
    /// Runs on its own thread for the lifetime of the "All Displays" source. Owns <see cref="_ddaSource"/>
    /// and <see cref="_converter"/> exclusively while running: no other thread touches them, and (critically)
    /// only this thread disposes them, in its own <c>finally</c>, once it has actually stopped using them.
    /// <see cref="Stop"/> only signals <see cref="_ddaStopping"/> and waits — it must never dispose these out
    /// from under a thread that might still be inside <c>AcquireNextFrame</c>/<c>Convert</c>. Any exception
    /// (a GPU hiccup, a dead output, etc.) is caught here so it degrades capture instead of taking the whole
    /// process down, since unhandled exceptions on a non-UI thread terminate the app by default.
    /// </summary>
    private void DdaPumpLoop()
    {
        DesktopDuplicationCaptureSource? ddaSource = _ddaSource;
        Nv12Converter? converter = _converter;
        ID3D11DeviceContext? context = _context;
        ID3D11Device? device = _device;
        try
        {
            while (!_ddaStopping)
            {
                ID3D11Texture2D canvas = ddaSource!.AcquireNextFrame(timeoutMs: 16);

                // Same throttle as OnFrameArrived (WGC path) — AcquireNextFrame returns as soon as the
                // desktop changes, which can be far faster than the recording/preview's own target fps.
                if (!_rateLimiter.ShouldAccept(Stopwatch.GetTimestamp()))
                {
                    continue;
                }

                converter!.Convert(canvas, _scratch);
                lock (_sync)
                {
                    (_scratch, _latest) = (_latest, _scratch);
                    _hasLatest = true;
                }

                Interlocked.Increment(ref _capturedFrames);
            }
        }
        catch (Exception ex)
        {
            try
            {
                Faulted?.Invoke(this, ex);
            }
            catch (Exception)
            {
                // A misbehaving subscriber must not prevent this thread from shutting down cleanly.
            }
        }
        finally
        {
            // Device/context for the DDA path are created by _ddaSource and used only by this thread
            // (via AcquireNextFrame/Convert) — dispose them here too, alongside the source and converter,
            // rather than in Stop(), for the same "only the last user disposes" reasoning.
            ddaSource?.Dispose();
            converter?.Dispose();
            context?.Dispose();
            device?.Dispose();
            _ddaThreadExited.Set();
        }
    }

    public bool TryGetLatestFrame(byte[] dest)
    {
        if (_softwareFallback is not null)
            return _softwareFallback.TryGetLatestFrame(dest);
        lock (_sync)
        {
            if (!_hasLatest)
            {
                return false;
            }

            Buffer.BlockCopy(_latest, 0, dest, 0, Nv12ByteSize);
            return true;
        }
    }

    public void SetWebcamOverlay(IWebcamFrameSource? source, RegionRect? rect)
    {
        if (_softwareFallback is not null) return;
        _webcamSource = source;
        _webcamRect = rect;
        _converter?.SetWebcamOverlay(source, rect);
    }

    public void SetBrightness(double value)
    {
        if (_softwareFallback is not null) return;
        _brightness = value;
        _converter?.SetBrightness(value);
    }

    public void SetZoomTarget(RegionRect? rect)
    {
        if (_softwareFallback is not null) return;
        _converter?.SetZoomTarget(rect);
    }

    public void SetBaseRect(RegionRect rect)
    {
        if (_softwareFallback is not null) return;
        _converter?.SetBaseRect(rect);
    }

    public void Stop()
    {
        // Guards against re-entrant Stop() calls: the window-closed callback (OnCaptureItemClosed) queues
        // Stop() on the thread pool, which can race a user-initiated Stop() (e.g. closing the recorded
        // window right as the user clicks Stop). Without this, both could observe IsRunning == true and both
        // run DisposeWgcResources(), double-releasing the same D3D11 COM objects. The second caller blocks
        // here until the first finishes, then sees IsRunning already false and returns immediately.
        lock (_stopLock)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;

            if (_softwareFallback is not null)
            {
                _softwareFallback.Dispose();
                _softwareFallback = null;
                _hasLatest = false;
                return;
            }

            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }

            // Barrier against an OnFrameArrived call already in flight when the unsubscribe above happened —
            // see the comment on OnFrameArrived's own lock for why this is safe and sufficient. Must be
            // _disposeGuard, not _sync: that's the lock OnFrameArrived now holds for the GPU work's whole
            // duration (_sync is only ever held briefly for the buffer swap, too short a barrier to trust).
            lock (_disposeGuard) { }

            StopCore();
        }
    }

    private void StopCore()
    {
        bool wasDda = _ddaThread is not null;
        if (wasDda)
        {
            // Signal and wait for confirmation the thread actually stopped — never dispose _ddaSource,
            // _converter, _context, or _device out from under it. If it doesn't exit in time (a stuck
            // AcquireNextFrame/GPU call), those resources are deliberately left alive: the thread's own
            // finally block (DdaPumpLoop) disposes them itself whenever it does eventually exit, and this
            // instance just drops its references below instead of double-disposing.
            _ddaStopping = true;
            bool exited = _ddaThreadExited.Wait(TimeSpan.FromSeconds(5));
            if (!exited)
            {
                Faulted?.Invoke(this, new TimeoutException(
                    "The desktop-duplication capture thread did not stop within 5 seconds; its resources will be released once it does."));
            }
        }

        if (!wasDda)
        {
            DisposeWgcResources();
        }

        _session = null;
        _item = null;
        _framePool = null;
        _ddaSource = null;
        _ddaThread = null;
        _converter = null;
        _context = null;
        _device = null;
        _hasLatest = false;
    }

    public void Dispose() => Stop();
}
