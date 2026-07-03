using RecMode.Core.Recording;
using Xunit;

namespace RecMode.Recording.Tests;

/// <summary>
/// Phase 0 placeholder. This project hosts the recording-service tests driven by fake
/// <c>IFrameSource</c>/<c>IAudioSource</c> once those exist (Phase 3+, plan §3.8). For now it holds a
/// single smoke test so the project is real and wired into the solution.
/// </summary>
public class PlaceholderTests
{
    [Fact]
    public void StateMachine_IsReachableFromRecordingTestProject()
    {
        var sm = new RecordingStateMachine(new ManualClock());
        sm.StartRecording();
        Assert.Equal(RecordingState.Recording, sm.State);
    }
}
