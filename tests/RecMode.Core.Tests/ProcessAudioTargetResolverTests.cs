using RecMode.Core.Recording;
using Xunit;

namespace RecMode.Core.Tests;

public class ProcessAudioTargetResolverTests
{
    private static readonly (string ProcessName, int ProcessId)[] Candidates =
    [
        ("chrome", 111),
        ("Spotify", 222),
        ("discord", 333),
    ];

    [Fact]
    public void Resolve_NullOrEmptyTarget_ReturnsNull()
    {
        Assert.Null(ProcessAudioTargetResolver.Resolve(Candidates, null));
        Assert.Null(ProcessAudioTargetResolver.Resolve(Candidates, ""));
        Assert.Null(ProcessAudioTargetResolver.Resolve(Candidates, "   "));
    }

    [Fact]
    public void Resolve_ExactMatch_ReturnsProcessId()
    {
        Assert.Equal(222, ProcessAudioTargetResolver.Resolve(Candidates, "Spotify"));
    }

    [Fact]
    public void Resolve_CaseInsensitiveMatch_ReturnsProcessId()
    {
        Assert.Equal(111, ProcessAudioTargetResolver.Resolve(Candidates, "CHROME"));
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        // App isn't currently running — a real, expected case (not an error).
        Assert.Null(ProcessAudioTargetResolver.Resolve(Candidates, "notepad"));
    }

    [Fact]
    public void Resolve_EmptyCandidateList_ReturnsNull()
    {
        Assert.Null(ProcessAudioTargetResolver.Resolve([], "chrome"));
    }
}
