namespace RecMode.Core.Errors;

/// <summary>
/// The four failure severities defined in the implementation plan (§3.6). Every failure in the
/// system maps to exactly one of these, and each has a defined UX channel (see the plan table).
/// </summary>
public enum ErrorSeverity
{
    /// <summary>Continue normally. UX: toast / InfoBar, auto-dismiss. (e.g. hotkey in use.)</summary>
    RecoverableWarning,

    /// <summary>Can't start the requested action. UX: InfoBar with explanation + suggested fix.</summary>
    BlockingError,

    /// <summary>Mid-recording, recording continues with reduced capability. UX: toolbar badge + toast.</summary>
    DegradedState,

    /// <summary>Recorded data at risk. UX: recovery dialog; never silently lose data.</summary>
    FatalFinalizationError,
}
