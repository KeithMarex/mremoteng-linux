using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace mRemoteNG.Avalonia.Sessions.Askpass;

/// <summary>
/// Hands a connection's stored password to ssh without putting it on the command line or in the environment, the
/// Linux counterpart of the named pipe the Windows app gives PuTTY (-pwfile). ssh runs this app as its SSH_ASKPASS
/// program; that helper asks this server over a Unix socket only the user can open, proving with a per-session token
/// that it belongs to this session. The password is given once, for the connection's own password prompt. Every other
/// prompt (a host key to confirm, a key passphrase, another password attempt) is answered by the user in the terminal,
/// or, for an ssh without one (an SSH tunnel), in a dialog.
/// </summary>
public sealed class AskpassServer : IDisposable
{
    public const string SocketVariable = "MREMOTENG_ASKPASS_SOCKET";
    public const string TokenVariable = "MREMOTENG_ASKPASS_TOKEN";
    internal const int MaxMessageLength = 64 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly Socket _listener;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string? _host;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private readonly Func<string, Task<string?>>? _askUser;
    private string? _password;

    private AskpassServer(Socket listener, string socketPath, string helperProgram, string? password, string? host,
                          Func<string, Task<string?>>? askUser)
    {
        _listener = listener;
        _password = password;
        _host = host;
        _askUser = askUser;
        SocketPath = socketPath;
        Environment = new Dictionary<string, string>
        {
            ["SSH_ASKPASS"] = helperProgram,
            ["SSH_ASKPASS_REQUIRE"] = "force",
            [SocketVariable] = socketPath,
            [TokenVariable] = _token
        };
        _ = AcceptAsync();
    }

    public string SocketPath { get; }

    /// <summary>The variables that make ssh ask the helper.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <param name="directory">Where the socket goes; created readable by the user only.</param>
    /// <param name="helperProgram">This app's executable, which acts as the helper when ssh starts it.</param>
    /// <param name="password">The stored password, if any.</param>
    /// <param name="host">The host the password belongs to, when known.</param>
    /// <param name="askUser">
    /// Answers the other prompts (null when the user cancels), for an ssh that has no terminal to ask in; without it,
    /// they are asked in the terminal.
    /// </param>
    public static AskpassServer Start(string directory, string helperProgram, string? password, string? host,
                                      Func<string, Task<string?>>? askUser = null)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The askpass helper needs Unix permissions on its socket.");

        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string socketPath = Path.Combine(directory, $"askpass-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}.sock");
        Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(4);
        }
        catch
        {
            listener.Dispose();
            File.Delete(socketPath);
            throw;
        }

        return new AskpassServer(listener, socketPath, helperProgram, password, host, askUser);
    }

    /// <summary>Decides the answer to one helper request: the stored password, or that the terminal asks.</summary>
    internal AskpassReply Answer(AskpassRequest request)
    {
        lock (_lock)
        {
            if (!TokenMatches(request) || _password is null || !OpenSshCommand.IsPasswordPromptFor(request.Prompt ?? string.Empty, _host))
                return AskpassReply.UseTerminal;

            string password = _password;
            _password = null; // once: a second prompt means the stored password was wrong
            return new AskpassReply(password, false);
        }
    }

    /// <summary>Like <see cref="Answer"/>, but asks the user, when there is no terminal, what the terminal would.</summary>
    internal async Task<AskpassReply> AnswerAsync(AskpassRequest request, CancellationToken cancellation)
    {
        AskpassReply reply = Answer(request);
        if (!reply.AskInTerminal || _askUser is null || !TokenMatches(request))
            return reply;
        return new AskpassReply(await _askUser(request.Prompt ?? string.Empty).WaitAsync(cancellation), false);
    }

    public void Dispose()
    {
        lock (_lock)
            _password = null;
        _stop.Cancel();
        _listener.Dispose();
        try
        {
            File.Delete(SocketPath);
        }
        catch (IOException)
        {
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                Socket client = await _listener.AcceptAsync(_stop.Token);
                _ = HandleAsync(client);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Stopped.
        }
    }

    private bool TokenMatches(AskpassRequest request) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Token ?? string.Empty), Encoding.UTF8.GetBytes(_token));

    private async Task HandleAsync(Socket client)
    {
        using (client)
        {
            try
            {
                await using NetworkStream stream = new(client);
                AskpassRequest? request;
                using (CancellationTokenSource timeout = Timeout())
                {
                    string? line = await AskpassProtocol.ReadLineAsync(stream, timeout.Token);
                    request = line is null ? null : JsonSerializer.Deserialize<AskpassRequest>(line);
                }

                // The user takes the time they need to answer; the helper waits.
                AskpassReply reply = request is null ? AskpassReply.UseTerminal : await AnswerAsync(request, _stop.Token);
                using (CancellationTokenSource timeout = Timeout())
                    await AskpassProtocol.WriteLineAsync(stream, JsonSerializer.Serialize(reply), timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or SocketException
                                           or InvalidDataException)
            {
                // A broken or slow helper gets no answer; ssh then sees an empty password.
            }
        }
    }

    private CancellationTokenSource Timeout()
    {
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(RequestTimeout);
        return timeout;
    }
}

/// <summary>What the helper sends: its token and ssh's prompt.</summary>
internal sealed record AskpassRequest(string? Token, string? Prompt);

/// <summary>
/// The answer: the password (or what the user answered in a dialog), or that the user answers the prompt in the
/// terminal. No password and no terminal means the user cancelled.
/// </summary>
internal sealed record AskpassReply(string? Password, bool AskInTerminal)
{
    public static AskpassReply UseTerminal { get; } = new(null, true);
}

/// <summary>One JSON message per line, in both directions.</summary>
internal static class AskpassProtocol
{
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        MemoryStream line = new();
        byte[] buffer = new byte[1];
        while (await stream.ReadAsync(buffer, cancellationToken) == 1)
        {
            if (buffer[0] == (byte)'\n')
                return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
            if (line.Length >= AskpassServer.MaxMessageLength)
                throw new InvalidDataException("The askpass message is too long.");
            line.WriteByte(buffer[0]);
        }

        return null;
    }

    public static async Task WriteLineAsync(Stream stream, string line, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
