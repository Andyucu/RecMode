using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>
/// Writes a process minidump via <c>dbghelp!MiniDumpWriteDump</c> (plan §3.6). Opt-in only; the crash
/// reporter gates the call on the user's setting. Uses a "with data segments" dump — richer than a
/// mini triage dump but far smaller than a full dump.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MinidumpWriter : IMinidumpWriter
{
    // MINIDUMP_TYPE flags (dbghelp.h).
    private const uint MiniDumpNormal = 0x0;
    private const uint MiniDumpWithDataSegs = 0x1;
    private const uint MiniDumpWithHandleData = 0x4;
    private const uint MiniDumpWithThreadInfo = 0x1000;

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeHandle hFile,
        uint dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    public bool TryWrite(string filePath)
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            using FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            uint dumpType = MiniDumpWithDataSegs | MiniDumpWithHandleData | MiniDumpWithThreadInfo;
            return MiniDumpWriteDump(
                process.Handle,
                (uint)Environment.ProcessId,
                stream.SafeFileHandle,
                dumpType,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
