using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions.Askpass;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using ProtocolType = mRemoteNG.Connection.Protocol.ProtocolType;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>The server a tunnel's session stands for: the session connects to 127.0.0.1, but host keys, certificates and prompts name the server.</summary>
public sealed record TunnelledServer(string Host, int Port, string TunnelName);

/// <summary>An open SSH tunnel to a connection's server; disposing it closes the tunnel.</summary>
public sealed class TunnelLease : IDisposable
{
    private static readonly ConditionalWeakTable<ConnectionInfo, TunnelledServer> Servers = new();
    private readonly ITunnelProcess _process;
    private readonly IDisposable? _askpass;
    private bool _disposed;

    internal TunnelLease(ConnectionInfo target, TunnelledServer server, ITunnelProcess process, IDisposable? askpass)
    {
        Target = target;
        Server = server;
        _process = process;
        _askpass = askpass;
        Servers.AddOrUpdate(target, server);
    }

    /// <summary>A copy of the connection that connects to the tunnel's local end, which sessions open instead.</summary>
    public ConnectionInfo Target { get; }

    public TunnelledServer Server { get; }

    internal bool IsDisposed => _disposed;

    /// <summary>The server <paramref name="connection"/> stands for, when it is a tunnel's <see cref="Target"/>.</summary>
    public static TunnelledServer? ServerOf(ConnectionInfo connection) => Servers.TryGetValue(connection, out TunnelledServer? server) ? server : null;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _process.Stop();
        _askpass?.Dispose();
    }
}

/// <summary>The ssh that keeps a tunnel open.</summary>
internal interface ITunnelProcess
{
    bool HasExited { get; }

    /// <summary>What ssh last wrote on its error output: why it failed.</summary>
    string? LastError { get; }

    /// <summary>Raised when ssh ends, on a worker thread.</summary>
    event EventHandler? Exited;

    void Stop();
}

/// <summary>
/// The SSH tunnels connections go through (their SSH Tunnel setting), as in the Windows app: ssh to the SSH connection
/// the setting names, forwarding a free local port to the connection's server, which the session then connects to.
/// Questions ssh asks on the way (an unknown host key, a password that is not stored) come up as dialogs.
/// </summary>
/// <param name="connections">The connections in the open file, where the tunnel connection is looked up by name.</param>
/// <param name="dispatch">Queues an action on the UI thread.</param>
public sealed class SshTunnels(SessionPrograms programs, string runtimeDirectory, IDialogService dialogs, Action<Action> dispatch,
                               Func<IEnumerable<ConnectionInfo>> connections) : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly List<TunnelLease> _kept = [];
    private int _asking;
    private DateTime _lastAnswer;

    /// <summary>How long ssh may take to open the tunnel, as in the Windows app; the user's time answering it does not count.</summary>
    internal TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Starts ssh; tests start a stand-in.</summary>
    internal Func<SessionPlan, IReadOnlyDictionary<string, string>, ITunnelProcess> StartProcess { get; set; } = OpenSshTunnelProcess.Start;

    /// <summary>Whether the connection goes through an SSH tunnel: it names one and has a server to reach.</summary>
    public static bool Uses(ConnectionInfo connection) =>
        connection is not ContainerInfo && !string.IsNullOrWhiteSpace(connection.SSHTunnelConnectionName) &&
        connection.Protocol is not (ProtocolType.AnyDesk or ProtocolType.WSL) &&
        !(connection.Protocol is ProtocolType.Terminal or ProtocolType.PowerShell && SessionPlanner.IsLocal(connection));

    /// <summary>Opens the tunnel <paramref name="target"/> names; its <see cref="TunnelLease.Target"/> is what the session connects to.</summary>
    /// <exception cref="SessionException">The tunnel could not be opened, with the reason.</exception>
    public async Task<TunnelLease> OpenAsync(ConnectionInfo target, CancellationToken cancellation)
    {
        string name = target.SSHTunnelConnectionName;
        // As on Windows: the first SSH connection with exactly that name.
        ConnectionInfo tunnel = connections().FirstOrDefault(connection => connection is not ContainerInfo && connection.Name == name &&
                                                                             connection.Protocol == ProtocolType.SSH2)
                                ?? throw new SessionException(string.Format(Language.SshTunnelConfigProblem, target.Name, name));
        if (target.Protocol == ProtocolType.FTP)
            throw new SessionException(UiText.FtpThroughSshTunnel);

        (string serverHost, int serverPort, Uri? webAddress) = ServerOf(target);
        int localPort = FreePort();
        SessionPlan ssh = Forwarding(OpenSshCommand.Build(tunnel, programs.Locate(programs.Ssh, "openssh-client")), localPort, serverHost, serverPort);
        foreach (string warning in ssh.Warnings)
            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{tunnel.Name}: {warning}");

        AskpassServer? askpass = programs.App is { } helper
            ? AskpassServer.Start(runtimeDirectory, helper, ssh.Password, ssh.PasswordHost, prompt => AskUserAsync(tunnel, prompt))
            : null;
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, UiText.OpeningSshTunnel(target.Name, tunnel.Name, ssh.ToString()));
        ITunnelProcess process;
        try
        {
            process = StartProcess(ssh, askpass?.Environment ?? new Dictionary<string, string>());
            await WaitUntilReadyAsync(process, localPort, target, tunnel, cancellation);
        }
        catch
        {
            askpass?.Dispose();
            throw;
        }

        ConnectionInfo copy = target.Clone();
        copy.Hostname = webAddress is null
            ? IPAddress.Loopback.ToString()
            : new UriBuilder(webAddress) { Host = IPAddress.Loopback.ToString(), Port = localPort }.Uri.ToString();
        copy.Port = localPort;
        copy.SSHTunnelConnectionName = string.Empty;
        TunnelLease lease = new(copy, new TunnelledServer(serverHost, serverPort, tunnel.Name), process, askpass);
        process.Exited += (_, _) => dispatch(() =>
        {
            if (!lease.IsDisposed)
                CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, UiText.SshTunnelEnded(target.Name, tunnel.Name, process.LastError));
        });
        return lease;
    }

    /// <summary>Keeps a tunnel open until the app closes: for a program this app does not watch, such as the web browser.</summary>
    public void KeepUntilExit(TunnelLease lease)
    {
        _kept.RemoveAll(kept => kept.IsDisposed);
        _kept.Add(lease);
    }

    public void Dispose()
    {
        foreach (TunnelLease lease in _kept)
            lease.Dispose();
        _kept.Clear();
    }

    /// <summary>
    /// The tunnel connection's ssh command line, with the local port forwarded to the server and nothing else: no
    /// shell, and ssh gives up when the port cannot be forwarded.
    /// </summary>
    internal static SessionPlan Forwarding(SessionPlan ssh, int localPort, string serverHost, int serverPort)
    {
        List<string> arguments = [.. ssh.Arguments];
        string server = serverHost.Contains(':') ? $"[{serverHost}]" : serverHost;
        arguments.InsertRange(arguments.IndexOf("--"),
                              ["-N", "-o", "ExitOnForwardFailure=yes", "-L",
                               string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{localPort}:{server}:{serverPort}")]);
        return ssh with { Arguments = arguments };
    }

    // The server and port the tunnel goes to; for a web address, the address too, whose host the copy replaces.
    private static (string Host, int Port, Uri? WebAddress) ServerOf(ConnectionInfo target)
    {
        if (target.Protocol is ProtocolType.HTTP or ProtocolType.HTTPS)
        {
            Uri address = WebAddress.For(target);
            return (address.Host, address.Port, address);
        }

        return (SessionPlanner.Host(target), SessionPlanner.Port(target), null);
    }

    private static int FreePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task WaitUntilReadyAsync(ITunnelProcess process, int localPort, ConnectionInfo target, ConnectionInfo tunnel,
                                           CancellationToken cancellation)
    {
        DateTime started = DateTime.UtcNow;
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    string reason = string.Format(Language.SshTunnelNotConnected, target.Name, tunnel.Name);
                    throw new SessionException(process.LastError is { } error ? $"{reason} ({error})" : reason);
                }

                if (await IsListeningAsync(localPort, cancellation))
                    return;
                // The time the user spends answering ssh's questions does not count.
                DateTime since = _lastAnswer > started ? _lastAnswer : started;
                if (Volatile.Read(ref _asking) == 0 && DateTime.UtcNow - since > ReadyTimeout)
                    throw new SessionException(string.Format(Language.SshTunnelPortNotReadyInTime, target.Name, tunnel.Name));
                await Task.Delay(PollInterval, cancellation);
            }
        }
        catch
        {
            process.Stop();
            throw;
        }
    }

    private static async Task<bool> IsListeningAsync(int port, CancellationToken cancellation)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        using TcpClient client = new();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            return false;
        }
    }

    // ssh's questions, asked on the askpass server's thread: a host key to accept is confirmed, anything else (a
    // password, a key passphrase, a one-time code) typed in hidden.
    private Task<string?> AskUserAsync(ConnectionInfo tunnel, string prompt)
    {
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref _asking);
        dispatch(async () =>
        {
            try
            {
                string title = UiText.SshTunnelTitle(tunnel.Name);
                answer.SetResult(IsQuestion(prompt)
                    ? await dialogs.ConfirmAsync(title, prompt.Trim()) ? "yes" : "no"
                    : (await dialogs.AskSecretAsync(prompt.Trim()))?.ConvertToUnsecureString());
            }
            catch (Exception ex)
            {
                answer.SetException(ex);
            }
            finally
            {
                _lastAnswer = DateTime.UtcNow;
                Interlocked.Decrement(ref _asking);
            }
        });
        return answer.Task;
    }

    // ssh's host key confirmation: "... (yes/no/[fingerprint])?" or "Please type 'yes' or 'no': ".
    private static bool IsQuestion(string prompt) =>
        prompt.Contains("(yes/no", StringComparison.OrdinalIgnoreCase) || prompt.Contains("'yes'", StringComparison.Ordinal);
}

/// <summary>ssh running a tunnel, without a terminal: its questions go to the askpass helper.</summary>
internal sealed class OpenSshTunnelProcess : ITunnelProcess
{
    private readonly Process _process;
    private volatile string? _lastError;

    private OpenSshTunnelProcess(Process process)
    {
        _process = process;
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _lastError = e.Data.Trim();
        };
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
    }

    public bool HasExited => _process.HasExited;

    public string? LastError => _lastError;

    public event EventHandler? Exited;

    public static ITunnelProcess Start(SessionPlan plan, IReadOnlyDictionary<string, string> environment)
    {
        ProcessStartInfo start = new(plan.Program, plan.Arguments)
        {
            UseShellExecute = false,
            // No terminal: ssh asks the askpass helper, which asks the app, never the terminal the app was started from.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach ((string name, string value) in environment)
            start.Environment[name] = value;

        Process process = new() { StartInfo = start };
        OpenSshTunnelProcess tunnel = new(process);
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new SessionException(UiText.CouldNotStart(Path.GetFileName(plan.Program), ex.Message));
        }

        process.StandardInput.Close();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return tunnel;
    }

    public void Stop()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
