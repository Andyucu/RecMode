using RecMode.Core.Settings;
using Xunit;

namespace RecMode.Core.Tests;

public class RecordingProfilesTests
{
    private static readonly string[] ExpectedNames =
    [
        "Tutorial (Balanced quality, 30 fps)",
        "Gameplay (High quality, 60 fps)",
        "Meeting (Standard quality, 30 fps)",
        "Bug report (Small file, 30 fps)",
        "Quick clip (Low quality, 15 fps, no audio)",
        "Archive (Maximum quality, 60 fps, lossless audio)",
    ];

    [Fact]
    public void BuiltIn_HasThePlanNamedPresets()
    {
        string[] names = [.. RecordingProfiles.BuiltIn.Select(p => p.Name)];
        Assert.Equal(ExpectedNames, names);
    }

    [Fact]
    public void BuiltIn_NamesAreUnique()
    {
        var names = RecordingProfiles.BuiltIn.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void BuiltIn_AllMarkedBuiltIn()
    {
        Assert.All(RecordingProfiles.BuiltIn, p => Assert.True(p.IsBuiltIn));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void BuiltIn_QualityAndFrameRateAreInRange(RecordingProfile profile)
    {
        Assert.InRange(profile.Quality, 0, 100);
        Assert.True(profile.FrameRate > 0);
        Assert.True(profile.AudioBitrateKbps > 0);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void BuiltIn_ContainerIsCompatibleWithH264(RecordingProfile profile)
    {
        // No built-in profile should ever produce an invalid codec/container pre-flight block for the
        // default H.264 encoder, regardless of what encoder the user has actually selected.
        Assert.True(MediaCompatibility.IsVideoCompatible(VideoCodec.H264, profile.Container));
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var profile = new RecordingProfile { Name = "My preset" };
        Assert.Equal("My preset", profile.ToString());
    }

    public static IEnumerable<object[]> Profiles => RecordingProfiles.BuiltIn.Select(p => new object[] { p });
}
