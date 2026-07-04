using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace RecMode.Capture;

/// <summary>
/// Win32/WinRT interop for WGC capture, productionized from the Phase 0.5 spike: enumerate monitors,
/// create a D3D11 device on the discrete GPU with video support, build a WGC capture item for a specific
/// monitor, and unwrap capture-frame surfaces to D3D11 textures.
/// </summary>
internal static class CaptureInterop
{
    private static readonly Guid ID3D11Texture2D_IID = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid GraphicsCaptureItem_IID = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

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

    public static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var results = new List<MonitorInfo>();
        int index = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref mi))
            {
                index++;
                int w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                bool primary = (mi.dwFlags & 1) != 0; // MONITORINFOF_PRIMARY
                results.Add(new MonitorInfo
                {
                    Handle = hMon,
                    DeviceName = mi.szDevice,
                    DisplayName = $"Display {index} ({w} × {h}){(primary ? " · primary" : "")}",
                    Width = w,
                    Height = h,
                    IsPrimary = primary,
                });
            }
            return true;
        }, IntPtr.Zero);

        return results;
    }

    public static (ID3D11Device Device, ID3D11DeviceContext Context) CreateDevice()
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

        try
        {
            FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
            DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
            D3D11.D3D11CreateDevice(best, DriverType.Unknown, flags, levels,
                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            return (device, context);
        }
        finally
        {
            best.Dispose();
        }
    }

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

    public static GraphicsCaptureItem CreateItemForMonitor(nint hMonitor)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        Guid iid = GraphicsCaptureItem_IID;
        IntPtr itemPtr = interop.CreateForMonitor(hMonitor, ref iid);
        try
        {
            return GraphicsCaptureItem.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }

    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = ID3D11Texture2D_IID;
        IntPtr texPtr = access.GetInterface(ref iid);
        return new ID3D11Texture2D(texPtr);
    }
}
