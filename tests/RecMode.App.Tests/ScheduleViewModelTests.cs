using RecMode.App.Services;
using RecMode.App.ViewModels;
using RecMode.Core.Settings;
using Xunit;

namespace RecMode.App.Tests;

public class ScheduleViewModelTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public RecModeSettings Current { get; } = new();
        public event EventHandler? SettingsChanged { add { } remove { } }
        public void Load() { }
        public void Save() { }
        public void RequestSave() { }
    }

    /// <summary>Returns whatever <see cref="Result"/> is set to, without showing any real UI.</summary>
    private sealed class FakeEditor : IScheduleEditor
    {
        public bool Result { get; set; } = true;
        public bool Edit(ScheduleItem item) => Result;
    }

    [Fact]
    public void NewSchedule_AddsItWhenTheEditorIsSaved()
    {
        var settings = new FakeSettings();
        var editor = new FakeEditor { Result = true };
        var vm = new ScheduleViewModel(settings, editor);

        vm.NewScheduleCommand.Execute(null);

        Assert.Single(vm.Schedules);
        Assert.Single(settings.Current.Schedules);
    }

    [Fact]
    public void NewSchedule_DiscardsItWhenTheEditorIsCancelled()
    {
        var settings = new FakeSettings();
        var editor = new FakeEditor { Result = false };
        var vm = new ScheduleViewModel(settings, editor);

        vm.NewScheduleCommand.Execute(null);

        Assert.Empty(vm.Schedules);
        Assert.Empty(settings.Current.Schedules);
    }
}
