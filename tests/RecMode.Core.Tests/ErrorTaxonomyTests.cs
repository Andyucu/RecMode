using RecMode.Core.Errors;
using Xunit;

namespace RecMode.Core.Tests;

public class ErrorTaxonomyTests
{
    [Fact]
    public void Factories_SetSeverity()
    {
        Assert.Equal(ErrorSeverity.RecoverableWarning, RecModeError.Warning("c", "m").Severity);
        Assert.Equal(ErrorSeverity.BlockingError, RecModeError.Blocking("c", "m").Severity);
        Assert.Equal(ErrorSeverity.DegradedState, RecModeError.Degraded("c", "m").Severity);
        Assert.Equal(ErrorSeverity.FatalFinalizationError, RecModeError.Fatal("c", "m").Severity);
    }

    [Fact]
    public void Reporter_RaisesEvent_AndRetainsRecent()
    {
        var reporter = new ErrorReporter();
        RecModeError? captured = null;
        reporter.ErrorReported += (_, e) => captured = e;

        reporter.Block("encoder.init-failed", "Encoder failed to start", "Try software encoding");

        Assert.NotNull(captured);
        Assert.Equal("encoder.init-failed", captured!.Code);
        Assert.Single(reporter.Recent);
        Assert.Equal(ErrorSeverity.BlockingError, reporter.Recent[0].Severity);
    }

    [Fact]
    public void Reporter_BoundsRetainedHistory()
    {
        var reporter = new ErrorReporter();
        for (int i = 0; i < 500; i++)
        {
            reporter.Warn($"code.{i}", "msg");
        }

        Assert.True(reporter.Recent.Count <= 200);
    }
}
