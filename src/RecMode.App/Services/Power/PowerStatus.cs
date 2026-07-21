using System.Runtime.InteropServices;

namespace RecMode.App.Services;

/// <summary>System power state, used for the on-battery pre-flight warning (plan §3.6 / Phase 9 battery warning).</summary>
public interface IPowerStatus
{
    /// <summary>True when running on battery (AC disconnected). False if plugged in or unknown.</summary>
    bool IsOnBattery { get; }

    /// <summary>Remaining battery charge 0–100, or null if unknown / no battery.</summary>
    int? BatteryPercent { get; }
}

/// <summary>Default <see cref="IPowerStatus"/> over the Win32 <c>GetSystemPowerStatus</c>.</summary>
public sealed class PowerStatus : IPowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;      // 0 = offline (battery), 1 = online, 255 = unknown
        public byte BatteryFlag;
        public byte BatteryLifePercent; // 0–100, or 255 = unknown
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public bool IsOnBattery => GetSystemPowerStatus(out SystemPowerStatus s) && s.AcLineStatus == 0;

    public int? BatteryPercent =>
        GetSystemPowerStatus(out SystemPowerStatus s) && s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
}
