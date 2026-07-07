using RecMode.Core.Recording;
using Xunit;

namespace RecMode.Core.Tests;

public class RecordingStateMachineTests
{
    private static RecordingStateMachine NewMachine(out ManualClock clock)
    {
        clock = new ManualClock();
        return new RecordingStateMachine(clock);
    }

    [Fact]
    public void StartsIdle()
    {
        var sm = new RecordingStateMachine(new ManualClock());
        Assert.Equal(RecordingState.Idle, sm.State);
        Assert.Equal(TimeSpan.Zero, sm.Elapsed);
        Assert.False(sm.IsActive);
    }

    [Fact]
    public void HappyPath_Record_Stop_Finalize_Idle()
    {
        var sm = NewMachine(out _);
        var seen = new List<RecordingState>();
        sm.StateChanged += (_, e) => seen.Add(e.Current);

        sm.StartRecording();
        sm.Stop();
        sm.CompleteFinalization();

        Assert.Equal(
            [RecordingState.Recording, RecordingState.Finalizing, RecordingState.Idle],
            seen);
        Assert.Equal(RecordingState.Idle, sm.State);
    }

    [Fact]
    public void DoubleStart_IsRejected()
    {
        var sm = NewMachine(out _);
        sm.StartRecording();
        Assert.Throws<InvalidOperationException>(sm.StartRecording);
    }

    [Fact]
    public void Stop_FromIdle_IsRejected()
    {
        var sm = NewMachine(out _);
        Assert.Throws<InvalidOperationException>(sm.Stop);
    }

    [Fact]
    public void Elapsed_ExcludesPausedTime()
    {
        var sm = NewMachine(out ManualClock clock);
        sm.StartRecording();          // t=0

        clock.Advance(TimeSpan.FromSeconds(10));
        sm.Pause();                    // active = 10s
        clock.Advance(TimeSpan.FromSeconds(5)); // paused span (should not count)
        Assert.Equal(TimeSpan.FromSeconds(10), sm.Elapsed);

        sm.Resume();
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(14), sm.Elapsed); // 10 + 4, the 5s pause excluded
    }

    [Fact]
    public void ToMediaPts_IsGaplessAcrossPause()
    {
        var sm = NewMachine(out ManualClock clock);
        sm.StartRecording(); // start anchor at clock=0

        clock.Advance(TimeSpan.FromSeconds(10));
        // A frame captured at t=10s → media PTS 10s.
        Assert.Equal(TimeSpan.FromSeconds(10), sm.ToMediaPts(TimeSpan.FromSeconds(10)));

        sm.Pause();
        clock.Advance(TimeSpan.FromSeconds(5)); // 5s paused
        sm.Resume();

        // A frame captured at wall t=15s should map to media PTS 10s (no gap for the paused span).
        Assert.Equal(TimeSpan.FromSeconds(10), sm.ToMediaPts(TimeSpan.FromSeconds(15)));

        clock.Advance(TimeSpan.FromSeconds(3));
        // Wall t=18s → media PTS 13s (18 − 5 paused).
        Assert.Equal(TimeSpan.FromSeconds(13), sm.ToMediaPts(TimeSpan.FromSeconds(18)));
    }

    [Fact]
    public void CanStop_FromPaused()
    {
        var pausedMachine = NewMachine(out _);
        pausedMachine.StartRecording();
        pausedMachine.Pause();
        pausedMachine.Stop();
        Assert.Equal(RecordingState.Finalizing, pausedMachine.State);
    }

    [Fact]
    public void DirectStart_FromIdle_Works()
    {
        var sm = NewMachine(out _);
        sm.StartRecording(); // CLI / scheduler / interactive — always a direct Idle -> Recording jump; the
                             // pre-roll countdown (when enabled) runs entirely before this call.
        Assert.Equal(RecordingState.Recording, sm.State);
    }
}
