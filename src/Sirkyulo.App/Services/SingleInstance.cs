using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Sirkyulo.App.Services;

/// <summary>
/// Keeps exactly one Sirkyulo alive and funnels every other launch into it.
///
/// This is not an optional nicety: every group activation and every drag-drop starts a fresh
/// Sirkyulo.exe, because that is how Explorer invokes a shortcut. Those launches must hand
/// their arguments to the resident tray instance and exit, or each drop would spawn a second
/// tray icon and a second writer racing on groups.json.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Sirkyulo.SingleInstance";
    private const string PipeName = "Sirkyulo.Args";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _listenerCts;

    public bool IsPrimary { get; }

    /// <summary>Raised on a background thread when another launch forwards its arguments.</summary>
    public event Action<string[]>? ArgumentsReceived;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        IsPrimary = created;
    }

    /// <summary>Starts accepting forwarded arguments. Primary instance only.</summary>
    public void StartListening()
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance listens.");

        _listenerCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_listenerCts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // A fresh server instance per connection: simplest correct shape, and drops
                // arrive far too rarely for the allocation to matter.
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync(token).ConfigureAwait(false);

                var args = JsonSerializer.Deserialize<string[]>(payload);
                if (args is { Length: > 0 }) ArgumentsReceived?.Invoke(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Warn($"Dropped a forwarded activation: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Hands this launch's arguments to the running instance. Returns false if nobody answered,
    /// which means the caller should take over as primary rather than exiting silently.
    /// </summary>
    public static bool Forward(string[] args, int timeoutMs = 3000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);

            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(JsonSerializer.Serialize(args));
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not reach the running instance: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();

        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }

        _mutex.Dispose();
    }
}
