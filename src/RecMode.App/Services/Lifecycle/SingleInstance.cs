using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace RecMode.App.Services;

/// <summary>
/// Single-instance guard + command forwarding (plan §3 CLI/automation). The first instance owns a named
/// mutex and listens on a named pipe; a second launch forwards its command line to the owner and exits, so
/// e.g. <c>RecMode --record</c> or a second double-click drives the already-running app instead of spawning
/// a duplicate. The mutex is session-local (<c>Local\</c>), so instances are per-user, not machine-wide.
/// <para>
/// The pipe itself cannot be made session-local the same way — named pipes always live in one machine-wide
/// namespace regardless of any <c>Local\</c>/<c>Global\</c> prefix, which only affects kernel objects like
/// mutexes. Two defenses close the resulting trust gap instead: the pipe's DACL restricts it to the current
/// Windows user (so once our real pipe is listening, a different account's process can't open it), and the
/// client verifies the SID of whichever process actually answers before trusting it with the forwarded
/// command line — without that, another local account could pre-create a pipe of this same name before RecMode
/// ever launches, silently receive every forwarded command line meant for the real instance (a real second
/// launch would connect to the impostor, "succeed", and exit — a permanent, silent DoS with no error shown),
/// and read whatever CLI arguments a user forwards.
/// </para>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\RecMode.SingleInstance.Mutex";
    private const string PipeName = "RecMode.SingleInstance.Pipe";

    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int TokenQuery = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Attempts to become the primary instance. Returns true if this process now owns the single-instance
    /// slot; false if another instance already holds it. The mutex is intentionally never released — the OS
    /// reclaims it on process exit, which sidesteps the same-thread-release requirement.
    /// </summary>
    public bool TryAcquireOwnership()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        return createdNew;
    }

    /// <summary>Secondary instance: hand our command line to the running primary. Returns false if none
    /// answered, or if whatever answered doesn't belong to the current Windows user — a different account's
    /// process squatting this pipe name is treated identically to "no primary found" rather than trusted.</summary>
    public static bool TryForwardToPrimary(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);

            if (!IsOwnedByCurrentUser(client.SafePipeHandle))
            {
                Log.Warning("A process claiming to be the primary RecMode instance isn't owned by the current user; ignoring it");
                return false;
            }

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(string.Join('\n', args));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not forward command line to the primary instance");
            return false;
        }
    }

    /// <summary>True if the process on the other end of <paramref name="pipe"/> runs under the same Windows
    /// user as this process. Fails closed (false) on any error — an identity we can't verify is never trusted.</summary>
    private static bool IsOwnedByCurrentUser(SafeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out uint serverPid))
        {
            return false;
        }

        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, serverPid);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out IntPtr token))
            {
                return false;
            }

            try
            {
                using var serverIdentity = new WindowsIdentity(token);
                using WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
                return serverIdentity.User is not null && serverIdentity.User == currentIdentity.User;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Primary instance: begin accepting forwarded command lines. <paramref name="onArgs"/> runs on a background thread.</summary>
    public void StartListening(Action<string[]> onArgs)
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(onArgs, _cts.Token));
    }

    private static async Task ListenLoopAsync(Action<string[]> onArgs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = CreateSecurePipe();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                string payload = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                string[] args = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                onArgs(args);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Single-instance listener iteration failed; retrying shortly");
                try
                {
                    // Bounded backoff before retrying. Without this, a failure that recurs on every
                    // iteration — e.g. the pipe name is already held by a different Windows session's
                    // RecMode instance (pipe names, unlike the Local\ mutex, are machine-global rather than
                    // per-session, so fast user switching can hit this) — would otherwise spin this loop as
                    // fast as the CLR can throw and catch, pegging a CPU core and flooding the log for the
                    // rest of the session.
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Creates the listening pipe restricted (via DACL) to the current Windows user — see the class
    /// doc for why this and the client-side SID check are both necessary. Mirrors
    /// <c>FfmpegRecordingSession.CreateSecurePipe</c>'s identical pattern for the encoder pipes.</summary>
    private static NamedPipeServerStream CreateSecurePipe()
    {
        var security = new PipeSecurity();
        SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Couldn't resolve the current Windows user's SID.");
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, security);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mutex?.Dispose();
    }
}
