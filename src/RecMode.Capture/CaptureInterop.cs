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

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
    private delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_CLOAKED = 14;

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
                    X = mi.rcMonitor.Left,
                    Y = mi.rcMonitor.Top,
                    Width = w,
                    Height = h,
                    IsPrimary = primary,
                });
            }
            return true;
        }, IntPtr.Zero);

        return results;
    }

    public static IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var results = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            int len = GetWindowTextLength(hwnd);
            if (len == 0)
            {
                return true;
            }

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            // Skip DWM-cloaked windows (UWP ghosts, other virtual desktops).
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            var sb = new System.Text.StringBuilder(len + 1);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (!string.IsNullOrWhiteSpace(title))
            {
                results.Add(new WindowInfo { Handle = hwnd, Title = title });
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>
    /// Running apps with a visible window, usable as per-app audio capture targets (plan §7). One entry per
    /// process (first window found), excluding RecMode's own process. Same visibility/cloak filtering as
    /// <see cref="EnumerateWindows"/>.
    /// </summary>
    public static IReadOnlyList<AudioProcessTarget> EnumerateAudioProcesses()
    {
        var results = new List<AudioProcessTarget>();
        var seenPids = new HashSet<uint>();
        uint selfPid = (uint)Environment.ProcessId;

        EnumWindows((hwnd, unusedData) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            int len = GetWindowTextLength(hwnd);
            if (len == 0)
            {
                return true;
            }

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == selfPid || !seenPids.Add(pid))
            {
                return true;
            }

            string processName;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                return true; // process exited or access denied between enumeration and lookup
            }

            var sb = new System.Text.StringBuilder(len + 1);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (!string.IsNullOrWhiteSpace(title))
            {
                results.Add(new AudioProcessTarget { ProcessId = (int)pid, ProcessName = processName, WindowTitle = title });
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

    public static GraphicsCaptureItem CreateItemForMonitor(nint hMonitor) =>
        CreateItem(interop => interop.CreateForMonitor(hMonitor, ref _itemIid));

    public static GraphicsCaptureItem CreateItemForWindow(nint hWnd) =>
        CreateItem(interop => interop.CreateForWindow(hWnd, ref _itemIid));

    public static GraphicsCaptureItem CreateItem(CaptureTarget target) => target.Kind switch
    {
        CaptureKind.Window => CreateItemForWindow(target.Handle),
        _ => CreateItemForMonitor(target.Handle), // Monitor and Region both capture the whole monitor; Region crops in the NV12 pass
    };

    private static Guid _itemIid = GraphicsCaptureItem_IID;

    private static GraphicsCaptureItem CreateItem(Func<IGraphicsCaptureItemInterop, IntPtr> create)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        IntPtr itemPtr = create(interop);
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
