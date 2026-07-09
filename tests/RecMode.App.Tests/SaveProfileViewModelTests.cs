using RecMode.App.ViewModels;
using Xunit;

namespace RecMode.App.Tests;

public class SaveProfileViewModelTests
{
    [Fact]
    public void IsValid_TrueForNonBlankDefaultName()
    {
        var vm = new SaveProfileViewModel("Tutorial");
        Assert.True(vm.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_FalseForBlankName(string name)
    {
        var vm = new SaveProfileViewModel(name);
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void IsValid_ReactsToNameEdits()
    {
        var vm = new SaveProfileViewModel("Tutorial") { Name = "" };
        Assert.False(vm.IsValid);

        vm.Name = "Gameplay";
        Assert.True(vm.IsValid);
    }
}
