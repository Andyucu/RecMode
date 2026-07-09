using RecMode.App.ViewModels;
using Xunit;

namespace RecMode.App.Tests;

public class RecordViewModelFormattingTests
{
    [Theory]
    [InlineData(2048L, "2 KB")]
    [InlineData(5 * 1024 * 1024L, "5 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2.00 GB")]
    public void FormatBytes_PicksTheRightUnit(long bytes, string expected)
    {
        Assert.Equal(expected, RecordViewModel.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(5, "00:05")]
    [InlineData(72, "01:12")]
    [InlineData(3661, "01:01:01")]
    public void FormatElapsed_UsesHoursOnlyWhenAtLeastAnHour(int seconds, string expected)
    {
        Assert.Equal(expected, RecordViewModel.FormatElapsed(TimeSpan.FromSeconds(seconds)));
    }
}
