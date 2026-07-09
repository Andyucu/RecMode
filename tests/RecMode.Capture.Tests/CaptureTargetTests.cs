using RecMode.Capture;
using Xunit;

namespace RecMode.Capture.Tests;

public class CaptureTargetTests
{
    private static MonitorInfo Monitor(int x, int y, int w, int h, bool primary = false) => new()
    {
        Handle = 1,
        DisplayName = $"Display {x},{y}",
        DeviceName = @"\\.\DISPLAY1",
        X = x,
        Y = y,
        Width = w,
        Height = h,
        IsPrimary = primary,
    };

    [Fact]
    public void FromAllDisplays_SingleMonitor_BoundsMatchItExactly()
    {
        var target = CaptureTarget.FromAllDisplays([Monitor(0, 0, 1920, 1080, primary: true)]);

        Assert.Equal(CaptureKind.AllDisplays, target.Kind);
        Assert.Equal(new RegionRect(0, 0, 1920, 1080), target.VirtualDesktopBounds);
    }

    [Fact]
    public void FromAllDisplays_TwoMonitorsSideBySideAtOrigin_UnionsWidth()
    {
        var target = CaptureTarget.FromAllDisplays(
        [
            Monitor(0, 0, 1920, 1080, primary: true),
            Monitor(1920, 0, 2560, 1440),
        ]);

        Assert.Equal(new RegionRect(0, 0, 1920 + 2560, 1440), target.VirtualDesktopBounds);
    }

    [Fact]
    public void FromAllDisplays_MonitorWithNegativeOrigin_ShiftsBoundsToStartAtItsMin()
    {
        // A secondary monitor positioned to the left of/above the primary has negative virtual-desktop
        // coordinates — a real Windows multi-monitor arrangement, not an edge case to special-case away.
        var target = CaptureTarget.FromAllDisplays(
        [
            Monitor(-1920, 0, 1920, 1080),
            Monitor(0, 0, 2560, 1440, primary: true),
        ]);

        Assert.Equal(new RegionRect(-1920, 0, 1920 + 2560, 1440), target.VirtualDesktopBounds);
    }

    [Fact]
    public void FromAllDisplays_VerticallyStackedMonitors_UnionsHeight()
    {
        var target = CaptureTarget.FromAllDisplays(
        [
            Monitor(0, 0, 1920, 1080, primary: true),
            Monitor(0, 1080, 1920, 1080),
        ]);

        Assert.Equal(new RegionRect(0, 0, 1920, 2160), target.VirtualDesktopBounds);
    }

    [Fact]
    public void FromAllDisplays_SetsDisplayNameAndZeroHandle()
    {
        var target = CaptureTarget.FromAllDisplays([Monitor(0, 0, 1920, 1080, primary: true)]);

        Assert.Equal("All Displays", target.DisplayName);
        Assert.Equal(nint.Zero, target.Handle);
    }
}
