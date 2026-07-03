namespace RecMode.Core.Infrastructure;

/// <summary>
/// Writes a process minidump to a file (implemented in RecMode.Interop via dbghelp). Opt-in only
/// (plan §3.6 — privacy is a feature); the crash reporter calls this only when the user enabled dumps.
/// </summary>
public interface IMinidumpWriter
{
    /// <summary>Writes a minidump for the current process. Returns true on success. Never throws.</summary>
    bool TryWrite(string filePath);
}

/// <summary>No-op writer used when minidumps are disabled or the platform lacks support.</summary>
public sealed class NullMinidumpWriter : IMinidumpWriter
{
    public bool TryWrite(string filePath) => false;
}
