namespace RecMode.Core.Infrastructure;

/// <summary>
/// Writes a process minidump to a file (implemented in RecMode.App.Services via dbghelp). Opt-in only
/// (plan §3.6 — privacy is a feature); the crash reporter calls this only when the user enabled dumps.
/// </summary>
public interface IMinidumpWriter
{
    /// <summary>Writes a minidump for the current process. Returns true on success. Never throws.</summary>
    bool TryWrite(string filePath);
}
