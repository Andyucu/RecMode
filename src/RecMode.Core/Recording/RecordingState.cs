namespace RecMode.Core.Recording;

/// <summary>
/// States of the single recording state machine (plan §3.7):
/// <code>Idle → Countdown → Recording ⇄ Paused → Finalizing → Idle</code>
/// with a <see cref="Degraded"/> branch off Recording. Every UI surface (main window, compact widget,
/// toolbar, tray, hotkeys, CLI) drives this one machine and observes its state.
/// </summary>
public enum RecordingState
{
    /// <summary>Not recording. The resting state.</summary>
    Idle,

    /// <summary>3-2-1 countdown before capture begins; cancellable back to <see cref="Idle"/>.</summary>
    Countdown,

    /// <summary>Actively capturing and encoding.</summary>
    Recording,

    /// <summary>Recording, but frame/sample feeding is frozen and wall-clock offset is held.</summary>
    Paused,

    /// <summary>Still recording, with reduced capability (e.g. a source dropped or hw→sw encoder fallback).</summary>
    Degraded,

    /// <summary>Flushing encoder, faststart/remux, writing the library entry, raising the toast.</summary>
    Finalizing,
}
