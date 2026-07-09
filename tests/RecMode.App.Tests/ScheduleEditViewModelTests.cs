using RecMode.App.ViewModels;
using RecMode.Core.Settings;
using Xunit;

namespace RecMode.App.Tests;

public class ScheduleEditViewModelTests
{
    private static readonly string[] ProfileNames = ["Tutorial", "Gameplay"];

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
        var vm = new ScheduleEditViewModel(NewItem(), ProfileNames);
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void IsValid_FalseForBlankName()
    {
        var vm = new ScheduleEditViewModel(NewItem(), ProfileNames) { Name = "   " };
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
        var vm = new ScheduleEditViewModel(NewItem(), ProfileNames) { Time = time };
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_ToleratesSurroundingWhitespaceInTime()
    {
        var vm = new ScheduleEditViewModel(NewItem(), ProfileNames) { Time = " 09:00 " };
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void ApplyTo_CommitsTrimmedFieldsToTheTarget()
    {
        var target = NewItem();
        var vm = new ScheduleEditViewModel(target, ProfileNames)
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

    [Fact]
    public void ProfileOptions_PutsFollowRecordSettingsFirstThenEveryProfileName()
    {
        var vm = new ScheduleEditViewModel(NewItem(), ProfileNames);
        Assert.Equal([ScheduleEditViewModel.FollowRecordSettingsOption, "Tutorial", "Gameplay"], vm.ProfileOptions);
    }

    [Fact]
    public void SelectedProfileOption_DefaultsToFollowRecordSettingsWhenSourceHasNoProfile()
    {
        var vm = new ScheduleEditViewModel(NewItem(), ProfileNames);
        Assert.Equal(ScheduleEditViewModel.FollowRecordSettingsOption, vm.SelectedProfileOption);
    }

    [Fact]
    public void SelectedProfileOption_PreselectsTheSourcesBoundProfile()
    {
        var item = NewItem();
        item.ProfileName = "Gameplay";
        var vm = new ScheduleEditViewModel(item, ProfileNames);
        Assert.Equal("Gameplay", vm.SelectedProfileOption);
    }

    [Fact]
    public void SelectedProfileOption_FallsBackToFollowRecordSettingsWhenTheBoundProfileNoLongerExists()
    {
        var item = NewItem();
        item.ProfileName = "Deleted profile";
        var vm = new ScheduleEditViewModel(item, ProfileNames);
        Assert.Equal(ScheduleEditViewModel.FollowRecordSettingsOption, vm.SelectedProfileOption);
    }

    [Fact]
    public void ApplyTo_SetsProfileNameWhenAProfileIsSelected()
    {
        var target = NewItem();
        var vm = new ScheduleEditViewModel(target, ProfileNames) { SelectedProfileOption = "Tutorial" };

        vm.ApplyTo(target);

        Assert.Equal("Tutorial", target.ProfileName);
    }

    [Fact]
    public void ApplyTo_SetsProfileNameToNullWhenFollowRecordSettingsIsSelected()
    {
        var target = NewItem();
        target.ProfileName = "Tutorial";
        var vm = new ScheduleEditViewModel(target, ProfileNames)
        {
            SelectedProfileOption = ScheduleEditViewModel.FollowRecordSettingsOption,
        };

        vm.ApplyTo(target);

        Assert.Null(target.ProfileName);
    }
}
