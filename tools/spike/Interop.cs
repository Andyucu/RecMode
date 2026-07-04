using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace RecMode.Spike;

/// <summary>
/// Win32/WinRT interop for the Phase 0.5 spike: pick the discrete GPU, make a D3D11 device with video
/// support, create a WGC capture item for a monitor, and unwrap capture-frame surfaces to D3D11 textures.
/// Disposable spike code.
/// </summary>
internal static class Interop
{
    private static readonly Guid ID3D11Texture2D_IID = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid GraphicsCaptureItem_IID = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    /// <summary>Creates a D3D11 device on the highest-VRAM (discrete) adapter, with BGRA + video support.</summary>
    public static (ID3D11Device Device, ID3D11DeviceContext Context, IDXGIAdapter1 Adapter) CreateDevice()
    {
        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        IDXGIAdapter1? best = null;
        ulong bestVram = 0;
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
        {
            AdapterDescription1 desc = adapter.Description1;
            bool isSoftware = (desc.Flags & AdapterFlags.Software) != 0;
            ulong vram = (ulong)desc.DedicatedVideoMemory;
            if (!isSoftware && vram >= bestVram)
            {
                best?.Dispose();
                best = adapter;
                bestVram = vram;
            }
            else
            {
                adapter.Dispose();
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException("No suitable DXGI adapter found.");
        }

        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;

        D3D11.D3D11CreateDevice(best, DriverType.Unknown, flags, levels,
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();

        Console.WriteLine($"[d3d] adapter = {best.Description1.Description.Trim()}  VRAM = {bestVram / (1024 * 1024)} MB");
        return (device, context, best);
    }

    /// <summary>Wraps a Vortice D3D11 device as a WinRT IDirect3DDevice for the WGC frame pool.</summary>
    public static IDirect3DDevice CreateWinRtDevice(ID3D11Device device)
    {
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr inspectable);
        if (hr < 0)
        {
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{hr:X8}");
        }

        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>Creates a WGC capture item for the primary monitor.</summary>
    public static GraphicsCaptureItem CreateItemForPrimaryMonitor()
    {
        IntPtr hmon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1 /* MONITOR_DEFAULTTOPRIMARY */);
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItem_IID;
        IntPtr itemPtr = interop.CreateForMonitor(hmon, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }

    /// <summary>Unwraps a capture frame's surface to the underlying D3D11 texture (no copy).</summary>
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = ID3D11Texture2D_IID;
        IntPtr texPtr = access.GetInterface(ref iid);
        return new ID3D11Texture2D(texPtr);
    }
}
