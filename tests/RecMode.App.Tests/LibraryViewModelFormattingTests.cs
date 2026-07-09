using RecMode.App.ViewModels;
using RecMode.Core.Library;
using Xunit;

namespace RecMode.App.Tests;

public class LibraryViewModelFormattingTests
{
    [Theory]
    [InlineData(500L, "500 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(5 * 1024 * 1024L, "5 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2.00 GB")]
    public void FormatSize_PicksTheRightUnit(long bytes, string expected)
    {
        Assert.Equal(expected, LibraryViewModel.FormatSize(bytes));
    }

    [Theory]
    [InlineData(5, "0:05")]
    [InlineData(72, "1:12")]
    [InlineData(3661, "1:01:01")]
    public void FormatDuration_UsesHoursOnlyWhenAtLeastAnHour(double seconds, string expected)
    {
        Assert.Equal(expected, LibraryViewModel.FormatDuration(seconds));
    }

    [Theory]
    [InlineData("H264", "H.264")]
    [InlineData("Hevc", "HEVC")]
    [InlineData("Av1", "AV1")]
    [InlineData("SomethingUnknown", "SomethingUnknown")]
    public void FriendlyCodec_MapsKnownCodecsAndPassesThroughUnknownOnes(string codec, string expected)
    {
        Assert.Equal(expected, LibraryViewModel.FriendlyCodec(codec));
    }

    [Fact]
    public void FormatDate_LabelsToday()
    {
        DateTime now = DateTime.Today.AddHours(14).AddMinutes(12);
        Assert.Equal($"Today {now:HH:mm}", LibraryViewModel.FormatDate(now));
    }

    [Fact]
    public void FormatDate_LabelsYesterday()
    {
        DateTime yesterday = DateTime.Today.AddDays(-1).AddHours(9);
        Assert.Equal($"Yesterday {yesterday:HH:mm}", LibraryViewModel.FormatDate(yesterday));
    }

    [Fact]
    public void FormatDate_UsesAbsoluteDateFurtherBack()
    {
        DateTime old = new(2026, 1, 2, 3, 4, 0);
        Assert.Equal("2026-01-02 03:04", LibraryViewModel.FormatDate(old));
    }

    [Fact]
    public void BuildMeta_ReturnsSizeAndDateOnlyWhenUnindexed()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            var f = new FileInfo(tmp);
            string meta = LibraryViewModel.BuildMeta(f, null);
            Assert.Equal($"{LibraryViewModel.FormatSize(f.Length)} · {LibraryViewModel.FormatDate(f.LastWriteTime)}", meta);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void BuildMeta_IncludesCodecResolutionAndDurationWhenIndexed()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            var f = new FileInfo(tmp);
            var entry = new LibraryIndexEntry(
                FileName: f.Name,
                Source: "Monitor",
                Codec: "H264",
                Container: "mp4",
                Width: 1920,
                Height: 1080,
                Fps: 60,
                DurationSeconds: 72,
                CreatedAt: DateTimeOffset.Now);

            string meta = LibraryViewModel.BuildMeta(f, entry);

            Assert.StartsWith("H.264 · 1920×1080 · 1:12 · ", meta);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
