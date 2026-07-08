using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace RecMode.App.Services;

/// <summary>
/// Single-instance guard + command forwarding (plan §3 CLI/automation). The first instance owns a named
/// mutex and listens on a named pipe; a second launch forwards its command line to the owner and exits, so
/// e.g. <c>RecMode --record</c> or a second double-click drives the already-running app instead of spawning
/// a duplicate. Names are session-local (<c>Local\</c>) so instances are per-user, not machine-wide.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\RecMode.SingleInstance.Mutex";
    private const string PipeName = "RecMode.SingleInstance.Pipe";

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

    /// <summary>Secondary instance: hand our command line to the running primary. Returns false if none answered.</summary>
    public static bool TryForwardToPrimary(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
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
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
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
                Log.Warning(ex, "Single-instance listener iteration failed; continuing");
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mutex?.Dispose();
    }
}
