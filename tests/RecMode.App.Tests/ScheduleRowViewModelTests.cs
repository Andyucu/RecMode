using RecMode.App.ViewModels;
using RecMode.Core.Settings;
using Xunit;

namespace RecMode.App.Tests;

public class ScheduleRowViewModelTests
{
    private static ScheduleItem NewItem() => new()
    {
        Name = "Morning stream",
        Recurrence = ScheduleRecurrence.Weekdays,
        Time = "09:00",
        DurationMinutes = 30,
        Enabled = true,
    };

    [Fact]
    public void WhenText_ComposesRecurrenceTimeAndDuration()
    {
        var vm = new ScheduleRowViewModel(NewItem(), persist: () => { });
        Assert.Equal("Weekdays · 09:00 · 30 min", vm.WhenText);
    }

    [Theory]
    [InlineData(ScheduleRecurrence.Once, "Once")]
    [InlineData(ScheduleRecurrence.Daily, "Daily")]
    [InlineData(ScheduleRecurrence.Weekdays, "Weekdays")]
    [InlineData(ScheduleRecurrence.Weekly, "Weekly")]
    public void WhenText_LabelsEveryRecurrenceKind(ScheduleRecurrence recurrence, string label)
    {
        var item = NewItem();
        item.Recurrence = recurrence;
        var vm = new ScheduleRowViewModel(item, persist: () => { });
        Assert.StartsWith(label + " · ", vm.WhenText);
    }

    [Fact]
    public void StateLabel_ReflectsEnabled()
    {
        var item = NewItem();
        var vm = new ScheduleRowViewModel(item, persist: () => { });

        Assert.Equal("On", vm.StateLabel);
        vm.Enabled = false;
        Assert.Equal("Off", vm.StateLabel);
    }

    [Fact]
    public void SettingEnabled_PersistsOnlyWhenValueActuallyChanges()
    {
        int persistCalls = 0;
        var vm = new ScheduleRowViewModel(NewItem(), persist: () => persistCalls++);

        vm.Enabled = true; // already true — no-op
        Assert.Equal(0, persistCalls);

        vm.Enabled = false;
        Assert.Equal(1, persistCalls);

        vm.Enabled = false; // unchanged again — no extra persist
        Assert.Equal(1, persistCalls);
    }

    [Fact]
    public void SourceText_ShowsFollowRecordSettingsWhenNoProfileIsBound()
    {
        var vm = new ScheduleRowViewModel(NewItem(), persist: () => { });
        Assert.Equal(ScheduleEditViewModel.FollowRecordSettingsOption, vm.SourceText);
    }

    [Fact]
    public void SourceText_ShowsTheBoundProfileName()
    {
        var item = NewItem();
        item.ProfileName = "Gameplay";
        var vm = new ScheduleRowViewModel(item, persist: () => { });
        Assert.Equal("Profile: Gameplay", vm.SourceText);
    }
}
