using RecMode.App.Services;
using Xunit;

namespace RecMode.App.Tests;

public class CommandLineOptionsTests
{
    [Fact]
    public void Parse_NoArgs_EverythingFalse()
    {
        var opts = CommandLineOptions.Parse([]);
        Assert.False(opts.Record);
        Assert.False(opts.Stop);
        Assert.False(opts.Screenshot);
        Assert.False(opts.Tray);
        Assert.False(opts.HasAction);
    }

    [Theory]
    [InlineData("--record")]
    [InlineData("-r")]
    [InlineData("--RECORD")]
    [InlineData(" --record ")]
    public void Parse_RecognisesRecordFlagCaseAndWhitespaceInsensitively(string arg)
    {
        var opts = CommandLineOptions.Parse([arg]);
        Assert.True(opts.Record);
        Assert.True(opts.HasAction);
    }

    [Theory]
    [InlineData("--stop")]
    [InlineData("-s")]
    public void Parse_RecognisesStopFlag(string arg)
    {
        Assert.True(CommandLineOptions.Parse([arg]).Stop);
    }

    [Fact]
    public void Parse_RecognisesScreenshotFlag()
    {
        Assert.True(CommandLineOptions.Parse(["--screenshot"]).Screenshot);
    }

    [Fact]
    public void Parse_RecognisesTrayFlag_AndTrayAloneIsNotAnAction()
    {
        var opts = CommandLineOptions.Parse(["--tray"]);
        Assert.True(opts.Tray);
        Assert.False(opts.HasAction);
    }

    [Fact]
    public void Parse_CombinesMultipleFlags()
    {
        var opts = CommandLineOptions.Parse(["--tray", "--record"]);
        Assert.True(opts.Tray);
        Assert.True(opts.Record);
    }

    [Fact]
    public void Parse_IgnoresUnknownArgs()
    {
        var opts = CommandLineOptions.Parse(["--unknown-future-flag", "--record"]);
        Assert.True(opts.Record);
        Assert.False(opts.Stop);
    }
}
