using RecMode.Core.Settings;
using Xunit;

namespace RecMode.Core.Tests;

public class ScheduleEvaluatorTests
{
    // A Wednesday at 09:00 local.
    private static readonly DateTimeOffset Wed0900 = new(2026, 7, 8, 9, 0, 0, TimeSpan.Zero);

    private static ScheduleItem Item(ScheduleRecurrence r, string time = "09:00", bool enabled = true,
        DateTimeOffset? lastFired = null) =>
        new() { Recurrence = r, Time = time, Enabled = enabled, LastFiredUtc = lastFired };

    [Fact]
    public void Daily_FiresAtMatchingMinute()
    {
        Assert.True(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Daily), Wed0900));
    }

    [Fact]
    public void Daily_DoesNotFireOffMinute()
    {
        Assert.False(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Daily), Wed0900.AddMinutes(1)));
        Assert.False(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Daily), Wed0900.AddHours(1)));
    }

    [Fact]
    public void Disabled_NeverFires()
    {
        Assert.False(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Daily, enabled: false), Wed0900));
    }

    [Fact]
    public void InvalidTime_NeverFires()
    {
        Assert.False(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Daily, time: "nope"), Wed0900));
    }

    [Fact]
    public void Dedup_DoesNotRefireWithinTheMinute()
    {
        // Already fired 20 s ago → skip even though the minute still matches.
        var item = Item(ScheduleRecurrence.Daily, lastFired: Wed0900.AddSeconds(-20));
        Assert.False(ScheduleEvaluator.IsDue(item, Wed0900));
    }

    [Fact]
    public void Weekdays_SkipsWeekend()
    {
        var sat = new DateTimeOffset(2026, 7, 11, 9, 0, 0, TimeSpan.Zero); // Saturday
        Assert.False(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Weekdays), sat));
        Assert.True(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Weekdays), Wed0900)); // Wednesday
    }

    [Fact]
    public void Once_FiresOnceThenNeverAgain()
    {
        Assert.True(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Once), Wed0900));
        // After it has fired (yesterday), it must not fire again.
        var fired = Item(ScheduleRecurrence.Once, lastFired: Wed0900.AddDays(-1));
        Assert.False(ScheduleEvaluator.IsDue(fired, Wed0900));
    }

    [Fact]
    public void Weekly_WaitsAWeek()
    {
        Assert.True(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Weekly), Wed0900)); // never fired
        Assert.False(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Weekly, lastFired: Wed0900.AddDays(-3)), Wed0900));
        Assert.True(ScheduleEvaluator.IsDue(Item(ScheduleRecurrence.Weekly, lastFired: Wed0900.AddDays(-7)), Wed0900));
    }
}
