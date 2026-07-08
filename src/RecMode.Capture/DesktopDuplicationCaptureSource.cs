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
    private readonly List<(IDXGIOutputDuplication Duplication, int OffsetX, int OffsetY)> _outputs = [];
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
        int minX = monitors.Min(m => m.X);
        int minY = monitors.Min(m => m.Y);
        VirtualWidth = monitors.Max(m => m.X + m.Width) - minX;
        VirtualHeight = monitors.Max(m => m.Y + m.Height) - minY;

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
            D3D11.D3D11CreateDevice(chosenAdapter, DriverType.Unknown, flags, levels,
                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            Device = device;
            Context = context;

            foreach ((IDXGIOutput output, MonitorInfo match) in chosenOutputs)
            {
                using (output)
                {
                    try
                    {
                        using IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
                        IDXGIOutputDuplication duplication = output1.DuplicateOutput(Device);
                        _outputs.Add((duplication, match.X - minX, match.Y - minY));
                    }
                    catch (Exception)
                    {
                        // Already duplicated by another process, or no desktop attached right now — that
                        // monitor's region just won't update (documented scope cut above).
                    }
                }
            }
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
        _canvas = Device.CreateTexture2D(canvasDesc);
    }

    /// <summary>Pulls the next composited frame (each output's <c>AcquireNextFrame</c> blocks up to
    /// <paramref name="timeoutMs"/>) and returns the shared canvas texture, valid until the next call.</summary>
    public ID3D11Texture2D AcquireNextFrame(int timeoutMs)
    {
        foreach ((IDXGIOutputDuplication duplication, int offsetX, int offsetY) in _outputs)
        {
            Result hr = duplication.AcquireNextFrame((uint)timeoutMs, out OutduplFrameInfo _, out IDXGIResource resource);
            if (!hr.Success)
            {
                continue; // timeout (DXGI_ERROR_WAIT_TIMEOUT) or transient error — keep this output's last pixels
            }

            using (resource)
            {
                using ID3D11Texture2D tex = resource.QueryInterface<ID3D11Texture2D>();
                Context.CopySubresourceRegion(_canvas, 0, (uint)offsetX, (uint)offsetY, 0, tex, 0, null);
            }

            duplication.ReleaseFrame();
        }

        return _canvas;
    }

    /// <summary>Disposes the duplications and the canvas. Does not dispose <see cref="Device"/>/<see cref="Context"/>
    /// — ownership of those follows the same convention as <see cref="WgcSessionFactory.Session"/>'s Device/Context,
    /// which the calling engine (<see cref="WgcCaptureEngine"/>/<see cref="WgcPreviewEngine"/>) disposes itself.</summary>
    public void Dispose()
    {
        foreach ((IDXGIOutputDuplication duplication, _, _) in _outputs)
        {
            duplication.Dispose();
        }
        _outputs.Clear();
        _canvas.Dispose();
    }
}
