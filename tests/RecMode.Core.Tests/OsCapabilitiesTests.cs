using RecMode.Core.Infrastructure;
using Xunit;

namespace RecMode.Core.Tests;

public class OsCapabilitiesTests
{
    [Theory]
    [InlineData(22631, true, true, true)]   // Win11 23H2
    [InlineData(22000, true, true, true)]   // Win11 RTM
    [InlineData(19045, false, false, true)] // Win10 22H2
    [InlineData(19041, false, false, true)] // Win10 2004 (floor)
    [InlineData(18363, false, false, false)]// Win10 1909 (below floor)
    public void CapabilitiesTrackBuildNumber(int build, bool win11, bool mica, bool meetsFloor)
    {
        var os = new OsCapabilities(build);

        Assert.Equal(win11, os.IsWindows11);
        Assert.Equal(mica, os.SupportsMicaBackdrop);
        Assert.Equal(mica, os.SupportsCaptureBorderSuppression);
        Assert.Equal(meetsFloor, os.MeetsMinimumOs);
        Assert.Equal(meetsFloor, os.SupportsProcessLoopbackAudio);
        Assert.Equal(meetsFloor, os.SupportsExcludeFromCapture);
    }
}
