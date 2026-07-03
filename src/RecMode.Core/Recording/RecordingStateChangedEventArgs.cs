namespace RecMode.Core.Recording;

/// <summary>Payload for <see cref="RecordingStateMachine.StateChanged"/>.</summary>
public sealed class RecordingStateChangedEventArgs(RecordingState previous, RecordingState current) : EventArgs
{
    public RecordingState Previous { get; } = previous;
    public RecordingState Current { get; } = current;
}
