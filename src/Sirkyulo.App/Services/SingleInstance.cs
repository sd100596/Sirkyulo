using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Sirkyulo.App.Services;

/// <summary>
/// Keeps exactly one Sirkyulo alive per user and funnels every other launch into it.
///
/// This is not an optional nicety: every group activation and every drag-drop starts a fresh
/// Sirkyulo.exe, because that is how Explorer invokes a shortcut. Those launches must hand
/// their arguments to the resident tray instance and exit, or each drop would spawn a second
/// tray icon and a second writer racing on groups.json.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// Anything larger than this is not a real activation. The pipe carries a JSON array of
    /// command-line arguments; without a cap a local client could stream unbounded data
    /// straight into memory.
    /// </summary>
    private const int MaxPayloadBytes = 64 * 1024;

    /// <summary>Stop retrying rather than spinning forever if the pipe can never be created.</summary>
    private const int MaxConsecutiveFailures = 10;

    /// <summary>
    /// UTF-8 without a byte-order mark.
    ///
    /// Encoding.UTF8 emits a BOM. StreamReader used to strip it silently, but the reader now
    /// works at the byte level to enforce a size cap, so a BOM would reach the JSON parser and
    /// fail every single activation with "0xEF is an invalid start of a value".
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly SecurityIdentifier _owner;
    private CancellationTokenSource? _listenerCts;

    public bool IsPrimary { get; }

    /// <summary>Raised on a background thread when another launch forwards its arguments.</summary>
    public event Action<string[]>? ArgumentsReceived;

    public SingleInstance()
    {
        _owner = WindowsIdentity.GetCurrent().User
                 ?? throw new InvalidOperationException("Cannot determine the current user.");

        // Both names are scoped to the user. The mutex was already per-session via Local\,
        // but a bare pipe name is machine-wide: two signed-in users would collide on it, and
        // whoever lost would have no working listener at all.
        _pipeName = $"Sirkyulo.Args.{_owner.Value}";

        _mutex = new Mutex(initiallyOwned: true, $@"Local\Sirkyulo.SingleInstance.{_owner.Value}", out var created);
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
        var failures = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = CreateServer();
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var payload = await ReadPayloadAsync(server, token).ConfigureAwait(false);
                failures = 0;
                if (payload is null) continue;

                var args = JsonSerializer.Deserialize<string[]>(payload);
                if (args is { Length: > 0 }) ArgumentsReceived?.Invoke(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Without a delay this loop spins a core and floods the log: a pipe that
                // cannot be created fails instantly, every iteration, forever.
                failures++;
                Log.Warn($"Dropped a forwarded activation ({failures}): {ex.Message}");

                if (failures >= MaxConsecutiveFailures)
                {
                    Log.Error("Giving up on the activation listener; group clicks will not reach this instance.");
                    return;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * failures), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Creates the server with an explicit ACL granting only this user.
    ///
    /// The default pipe security is permissive enough that another signed-in user could
    /// connect and post arbitrary arguments — which, since arguments can name files to absorb
    /// into a group, means moving another account's files around. This is the app's only real
    /// trust boundary, so it is closed explicitly rather than left to defaults.
    /// </summary>
    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(_owner, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    /// <summary>Reads at most <see cref="MaxPayloadBytes"/>, returning null if the sender exceeds it.</summary>
    private static async Task<string?> ReadPayloadAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[MaxPayloadBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }

        if (total > MaxPayloadBytes)
        {
            Log.Warn("Ignored an oversized activation payload.");
            return null;
        }

        if (total == 0) return null;

        // Tolerate a BOM from any sender, not just our own writer.
        var start = total >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
        return Utf8NoBom.GetString(buffer, start, total - start);
    }

    /// <summary>
    /// Hands this launch's arguments to the running instance. Returns false if nobody answered,
    /// which means the caller should take over as primary rather than exiting silently.
    /// </summary>
    public bool Forward(string[] args, int timeoutMs = 3000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(timeoutMs);

            using var writer = new StreamWriter(client, Utf8NoBom);
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
