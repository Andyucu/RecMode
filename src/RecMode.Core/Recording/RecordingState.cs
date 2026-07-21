namespace RecMode.Core.Recording;

/// <summary>
/// States of the single recording state machine (plan §3.7):
/// <code>Idle → Recording ⇄ Paused → Finalizing → Idle</code>
/// Every UI surface (main window, compact widget, toolbar, tray, hotkeys, CLI) drives this one machine and
/// observes its state. The pre-roll countdown and the "encoder degraded" signal are deliberately NOT states
/// here — the countdown runs entirely before <c>StartRecording()</c> is called (<see cref="RecordingState"/>
/// stays <see cref="Idle"/> throughout it; see <c>ICountdownController</c>/<c>CountdownWindow</c>), and
/// "degraded" is reported through the error taxonomy (<c>IErrorReporter.Degrade</c>) plus
/// <c>RecordingProgress.IsHealthy</c> while the state machine simply stays in <see cref="Recording"/> — the
/// recording never actually stops or changes state just because it's running below par.
/// </summary>
public enum RecordingState
{
    /// <summary>Not recording. The resting state.</summary>
    Idle,

    /// <summary>Actively capturing and encoding.</summary>
    Recording,

    /// <summary>Recording, but frame/sample feeding is frozen and wall-clock offset is held.</summary>
    Paused,

    /// <summary>Flushing encoder, faststart/remux, writing the library entry, raising the toast.</summary>
    Finalizing,
}
