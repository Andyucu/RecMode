using RecMode.App.ViewModels;
using RecMode.Core.Settings;
using Xunit;

namespace RecMode.App.Tests;

public class ScheduleEditViewModelTests
{
    private static ScheduleItem NewItem() => new()
    {
        Name = "Morning stream",
        Recurrence = ScheduleRecurrence.Weekdays,
        Time = "09:00",
        DurationMinutes = 30,
    };

    [Fact]
    public void IsValid_TrueForWellFormedNameAndTime()
    {
        var vm = new ScheduleEditViewModel(NewItem());
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void IsValid_FalseForBlankName()
    {
        var vm = new ScheduleEditViewModel(NewItem()) { Name = "   " };
        Assert.False(vm.IsValid);
    }

    [Theory]
    [InlineData("9:00")]
    [InlineData("25:00")]
    [InlineData("09:60")]
    [InlineData("not a time")]
    [InlineData("")]
    public void IsValid_FalseForMalformedTime(string time)
    {
        var vm = new ScheduleEditViewModel(NewItem()) { Time = time };
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_ToleratesSurroundingWhitespaceInTime()
    {
        var vm = new ScheduleEditViewModel(NewItem()) { Time = " 09:00 " };
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void ApplyTo_CommitsTrimmedFieldsToTheTarget()
    {
        var target = NewItem();
        var vm = new ScheduleEditViewModel(target)
        {
            Name = "  Evening stream  ",
            SelectedRecurrence = ScheduleRecurrence.Daily,
            Time = " 18:30 ",
            DurationMinutes = 60,
        };

        vm.ApplyTo(target);

        Assert.Equal("Evening stream", target.Name);
        Assert.Equal(ScheduleRecurrence.Daily, target.Recurrence);
        Assert.Equal("18:30", target.Time);
        Assert.Equal(60, target.DurationMinutes);
    }
}
