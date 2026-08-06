using Serilog;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RecMode.Capture;

/// <summary>
/// Composites every monitor output into one virtual-desktop-sized BGRA texture via DXGI Desktop Duplication —
/// the "All Displays" source (plan §1: "Full screen (per display + all displays)"). WGC has no native
/// multi-monitor capture item, so this uses the lower-level per-output duplication API instead, purely for
/// this one source; single-monitor capture still goes through WGC (<see cref="WgcSessionFactory"/>).
/// One <c>IDXGIOutputDuplication</c> per monitor, each Copy'd into its correct offset within a shared canvas
/// on every pull — a monitor with no new frame this cycle just keeps its last composited pixels.
/// <para>
/// Creates its own dedicated D3D11 device rather than reusing whatever device the WGC path picked
/// (<c>CaptureInterop.CreateDevice</c>'s "highest VRAM" heuristic doesn't care whether that adapter actually
/// owns any display output — and DXGI Desktop Duplication requires that it does). Found the hard way: this
/// dev machine enumerates the same physical GPU as two separate adapter entries with identical VRAM, and only
/// one of the two has any outputs attached (a WDDM linked-adapter artifact); picking "last on a VRAM tie"
/// silently chose the output-less one, producing all-black frames with no exception anywhere. So instead this
/// class enumerates every adapter, keeps whichever one owns the most of the target monitors' outputs, and
/// builds its device on that adapter specifically.
/// </para>
/// <para>Known, deliberate scope cut: a monitor genuinely driven by a different physical GPU than the one
/// chosen here still fails to duplicate and that region just never updates (fails closed, not a crash) — this
/// covers the rare case of a real split-GPU multi-monitor setup, as opposed to the single-GPU norm.</para>
/// </summary>
internal sealed class DesktopDuplicationCaptureSource : IDisposable
{
    private const int DxgiErrorAccessLost = unchecked((int)0x887A0026);
    private const int DxgiErrorWaitTimeout = unchecked((int)0x887A0027);

    /// <summary>Backoff between re-<c>DuplicateOutput</c> attempts after access loss, so a prolonged secure-
    /// desktop transition (or anything else that keeps returning ACCESS_LOST) doesn't turn every ~16ms
    /// <see cref="AcquireNextFrame"/> call into a failing DuplicateOutput syscall.</summary>
    private static readonly TimeSpan AccessLostRetryInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>One monitor's live duplication state. <see cref="Duplication"/> is mutable — access loss
    /// (DXGI_ERROR_ACCESS_LOST, a routine return on secure-desktop transitions, Ctrl+Alt+Del, display
    /// mode/resolution changes, and monitor sleep/wake, not an error condition) disposes and re-acquires it
    /// in place via the retained <see cref="Output"/>, rather than failing the whole capture source. Before
    /// this, any of those routine events permanently ended All-Displays capture for the rest of the recording
    /// (the pacer just kept duplicating the last good frame) — e.g. a 1s UAC prompt 30s into a 20-minute
    /// recording produced 19.5 minutes of a frozen desktop.</summary>
    private sealed class OutputState(IDXGIOutput1 output, IDXGIOutputDuplication? duplication, int offsetX, int offsetY)
    {
        public IDXGIOutput1 Output { get; } = output;
        public IDXGIOutputDuplication? Duplication { get; set; } = duplication;
        public int OffsetX { get; } = offsetX;
        public int OffsetY { get; } = offsetY;
        public long NextRetryTicks { get; set; }
    }

    private readonly List<OutputState> _outputs = [];
    private readonly ID3D11Texture2D _canvas;

    /// <summary>Device created on the adapter that actually owns the target monitors — callers (the NV12/BGRA
    /// conversion pipeline) must use this device, not a separately-created one, since the canvas texture and
    /// the duplicated frames both live on it.</summary>
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public int VirtualWidth { get; }
    public int VirtualHeight { get; }

    public DesktopDuplicationCaptureSource(IReadOnlyList<MonitorInfo> monitors)
    {
        VirtualDesktopLayout.Bounds bounds = VirtualDesktopLayout.Compute(monitors);
        VirtualWidth = bounds.Width;
        VirtualHeight = bounds.Height;

        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        IDXGIAdapter1? chosenAdapter = null;
        List<(IDXGIOutput Output, MonitorInfo Monitor)> chosenOutputs = [];

        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
        {
            var matched = new List<(IDXGIOutput, MonitorInfo)>();
            for (uint j = 0; adapter.EnumOutputs(j, out IDXGIOutput output).Success; j++)
            {
                MonitorInfo? match = monitors.FirstOrDefault(m => m.Handle == output.Description.Monitor);
                if (match is not null)
                {
                    matched.Add((output, match));
                }
                else
                {
                    output.Dispose();
                }
            }

            if (matched.Count > chosenOutputs.Count)
            {
                foreach ((IDXGIOutput o, MonitorInfo _) in chosenOutputs) { o.Dispose(); }
                chosenAdapter?.Dispose();
                chosenAdapter = adapter;
                chosenOutputs = matched;
            }
            else
            {
                foreach ((IDXGIOutput o, MonitorInfo _) in matched) { o.Dispose(); }
                adapter.Dispose();
            }
        }

        if (chosenAdapter is null)
        {
            throw new InvalidOperationException("No display adapter with a matching output was found for Desktop Duplication.");
        }

        using (chosenAdapter)
        {
            FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
            DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
            try
            {
                D3D11.D3D11CreateDevice(chosenAdapter, DriverType.Unknown, flags, levels,
                    out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
                Device = device;
                Context = context;
            }
            catch
            {
                // CheckError() throws before the loop below, which is what would otherwise have disposed
                // these — so every matched IDXGIOutput COM object leaked. Only reachable on the failure path,
                // but that's exactly the path that then falls back to GDI and may be retried.
                foreach ((IDXGIOutput output, MonitorInfo _) in chosenOutputs) { output.Dispose(); }
                throw;
            }

            foreach ((IDXGIOutput output, MonitorInfo match) in chosenOutputs)
            {
                using (output)
                {
                    // output1 is retained (not disposed here) so a later access-loss can re-DuplicateOutput
                    // from the same IDXGIOutput1 instead of failing the whole source — disposed in Dispose().
                    IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
                    (int offsetX, int offsetY) = VirtualDesktopLayout.OffsetOf(match, bounds);
                    try
                    {
                        IDXGIOutputDuplication duplication = output1.DuplicateOutput(Device);
                        _outputs.Add(new OutputState(output1, duplication, offsetX, offsetY));
                    }
                    catch (Exception ex)
                    {
                        // Already duplicated by another process, or no desktop attached right now — that
                        // monitor's region just won't update (documented scope cut above). Still keep output1
                        // around: AcquireNextFrame retries DuplicateOutput on its own backoff, so a transient
                        // "already duplicated" at startup can still recover once the other duplicator lets go.
                        Log.Warning(ex, "Desktop Duplication failed for monitor at ({X},{Y}); that region won't update",
                            match.X, match.Y);
                        _outputs.Add(new OutputState(output1, null, offsetX, offsetY));
                    }
                }
            }
        }

        if (_outputs.All(o => o.Duplication is null))
        {
            foreach (OutputState o in _outputs) { o.Output.Dispose(); }
            Context.Dispose();
            Device.Dispose();
            throw new InvalidOperationException("Desktop Duplication could not open any selected display output.");
        }

        var canvasDesc = new Texture2DDescription
        {
            Width = (uint)VirtualWidth,
            Height = (uint)VirtualHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            // Needs RenderTarget too, not just ShaderResource — the same VideoProcessorInputView gotcha
            // found and fixed for the webcam overlay upload texture (2026-07-06): CreateVideoProcessorInputView
            // throws E_INVALIDARG on a texture that's only ShaderResource-bound.
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        try
        {
            _canvas = Device.CreateTexture2D(canvasDesc);
        }
        catch
        {
            // Reachable for real: an oversized virtual desktop (several 4K+ monitors side by side can exceed
            // D3D11's max texture dimension) or a transient GPU-memory failure both throw here, after every
            // output above has already been successfully duplicated — DXGI output duplication is exclusive
            // per output, so leaving these live (the constructor never finishes, so Dispose() never runs)
            // meant every later All-Displays attempt hit "already duplicated" on every output for the rest of
            // the process, permanently stuck on the GDI fallback from what was really just one bad texture
            // allocation.
            foreach (OutputState o in _outputs)
            {
                o.Duplication?.Dispose();
                o.Output.Dispose();
            }
            Context.Dispose();
            Device.Dispose();
            throw;
        }
    }

    /// <summary>Pulls the next composited frame and returns the shared canvas texture, valid until the next
    /// call. Only the <em>first</em> output's <c>AcquireNextFrame</c> gets the full <paramref name="timeoutMs"/>
    /// wait; every other output uses a near-zero timeout instead of also blocking up to the full amount —
    /// looping N outputs each with the full timeout meant a pull cost up to N × timeoutMs when every monitor
    /// was idle (a 3-monitor All-Displays capture could cap out around 30fps even with a 60fps target, and a
    /// 4th monitor made it worse — the more monitors, the worse "All Displays" got). One monitor still gets a
    /// real, efficient blocking wait each pull so the loop doesn't busy-spin when the whole desktop is idle.
    /// An output that currently has no live duplication (never acquired one, or just lost access) is skipped
    /// for this pull — its region of the canvas simply keeps its last composited pixels, same as a monitor
    /// with no new frame this cycle.</summary>
    /// <param name="changed">True if at least one output actually copied new pixels into the canvas this
    /// pull. False means every output either had nothing new (DXGI_ERROR_WAIT_TIMEOUT) or has no live
    /// duplication right now — the canvas is byte-identical to what it was after the last call, so callers
    /// doing GPU work off the result (a VideoProcessorBlt + staging readback, ~7 MB/frame at 4096x1152) can
    /// skip it entirely on a static desktop instead of reprocessing pixels that didn't move. This used to be
    /// unreported, so the pump loop's only throttle was time-based — a fully idle All-Displays recording
    /// still paid the full convert+readback cost at the target fps regardless.</param>
    public ID3D11Texture2D AcquireNextFrame(int timeoutMs, out bool changed)
    {
        changed = false;
        bool isFirst = true;
        foreach (OutputState state in _outputs)
        {
            if (state.Duplication is null)
            {
                TryReacquire(state);
                if (state.Duplication is null)
                {
                    continue;
                }
            }

            // isFirst must only be consumed by an output that actually reaches a real AcquireNextFrame call
            // below, not by loop position. Consuming it earlier (for an output skipped above because it has
            // no live duplication) handed the real blocking wait to nothing — every remaining output,
            // including ones with a perfectly live duplication, then got timeoutMs=0 for the rest of this
            // pull, and this repeats every pull for as long as that one output stays duplication-less (which
            // is indefinitely for an unplugged monitor). The pump loop then calls this in a tight loop with no
            // real wait at all, spinning a full CPU core — precisely what the single-real-wait design above
            // exists to avoid.
            uint thisTimeoutMs = isFirst ? (uint)timeoutMs : 0;
            isFirst = false;

            Result hr = state.Duplication.AcquireNextFrame(thisTimeoutMs, out OutduplFrameInfo _, out IDXGIResource resource);
            if (!hr.Success)
            {
                if (hr.Code == DxgiErrorWaitTimeout)
                {
                    continue; // no new frame this cycle — harmless, keep the duplication
                }

                if (hr.Code == DxgiErrorAccessLost)
                {
                    // Routine, not fatal: secure-desktop transitions (UAC/Ctrl+Alt+Del), display mode/resolution
                    // changes, and monitor sleep/wake all surface as ACCESS_LOST. The duplication itself is
                    // permanently dead once this happens, but the output can simply be re-duplicated — previously
                    // this was treated as fatal for the whole source, permanently freezing that region (or, via
                    // WgcCaptureEngine/WgcPreviewEngine's Faulted handling, the whole capture) for the rest of the
                    // recording over something as brief as a one-second UAC prompt.
                    Log.Warning("Desktop Duplication lost access for the output at ({X},{Y}); will retry", state.OffsetX, state.OffsetY);
                    state.Duplication.Dispose();
                    state.Duplication = null;
                    state.NextRetryTicks = 0; // retry immediately next pull, not after the backoff
                    continue;
                }

                throw new InvalidOperationException($"Desktop Duplication failed (0x{hr.Code:X8}).");
            }

            try
            {
                using (resource)
                using (ID3D11Texture2D tex = resource.QueryInterface<ID3D11Texture2D>())
                {
                    Context.CopySubresourceRegion(_canvas, 0, (uint)state.OffsetX, (uint)state.OffsetY, 0, tex, 0, null);
                    changed = true;
                }
            }
            finally { state.Duplication.ReleaseFrame(); }
        }

        if (isFirst)
        {
            // isFirst is still true only if not one single output reached a real AcquireNextFrame call this
            // pull (every output is duplication-less right now, e.g. every monitor lost access at once) — in
            // that case nothing above ever waited at all, so sleep the timeout ourselves rather than let the
            // pump loop spin a full core calling this in a tight, wait-free cycle until something reacquires.
            Thread.Sleep(Math.Max(1, timeoutMs));
        }

        return _canvas;
    }

    /// <summary>Attempts to re-<c>DuplicateOutput</c> a monitor whose duplication was lost, honouring
    /// <see cref="AccessLostRetryInterval"/> so a still-locked/still-transitioning desktop doesn't turn every
    /// pull into a failing syscall.</summary>
    private void TryReacquire(OutputState state)
    {
        long now = Environment.TickCount64;
        if (now < state.NextRetryTicks)
        {
            return;
        }

        try
        {
            state.Duplication = state.Output.DuplicateOutput(Device);
        }
        catch (Exception ex)
        {
            state.NextRetryTicks = now + (long)AccessLostRetryInterval.TotalMilliseconds;
            Log.Debug(ex, "Desktop Duplication re-acquire failed for the output at ({X},{Y}); will retry in {Ms}ms",
                state.OffsetX, state.OffsetY, AccessLostRetryInterval.TotalMilliseconds);
        }
    }

    /// <summary>Disposes the duplications and the canvas. Does not dispose <see cref="Device"/>/<see cref="Context"/>
    /// — ownership of those follows the same convention as <see cref="WgcSessionFactory.Session"/>'s Device/Context,
    /// which the calling engine (<see cref="WgcCaptureEngine"/>/<see cref="WgcPreviewEngine"/>) disposes itself.</summary>
    public void Dispose()
    {
        foreach (OutputState state in _outputs)
        {
            state.Duplication?.Dispose();
            state.Output.Dispose();
        }
        _outputs.Clear();
        _canvas.Dispose();
    }
}
