using System.Net.Sockets;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using ProtocolType = mRemoteNG.Connection.Protocol.ProtocolType;

namespace mRemoteNG.Avalonia.Services;

public enum HostStatus
{
    /// <summary>Not checked (yet), or nothing to check: a folder, a local shell, an AnyDesk ID.</summary>
    Unknown,
    Online,
    Offline
}

/// <summary>
/// Shows in the connection tree which servers answer: every minute, a TCP connection to each connection's host and
/// port, at most 16 at a time and each (host, port) once. Runs on the UI thread, which only waits for the checks.
/// </summary>
public sealed class HostStatusMonitor : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);
    internal const int MaxParallelChecks = 16;
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _interval;
    private CancellationTokenSource? _running;
    private Func<IEnumerable<ConnectionNodeViewModel>> _nodes = () => [];

    public HostStatusMonitor(TimeSpan? interval = null) => _interval = interval ?? DefaultInterval;

    /// <summary>Checks a host and port: null when it answered, otherwise why not.</summary>
    internal Func<string, int, CancellationToken, Task<string?>> Probe { get; set; } = ProbeAsync;

    public bool IsRunning => _running is not null;

    /// <summary>Checks the nodes now and then every interval, until <see cref="Stop"/>; starting again checks at once.</summary>
    public void Start(Func<IEnumerable<ConnectionNodeViewModel>> nodes)
    {
        Stop();
        _nodes = nodes;
        _running = new CancellationTokenSource();
        _ = RunAsync(_running.Token);
    }

    /// <summary>Stops checking and forgets what the nodes showed.</summary>
    public void Stop()
    {
        if (_running is null)
            return;
        _running.Cancel();
        _running.Dispose();
        _running = null;
        foreach (ConnectionNodeViewModel node in _nodes())
            node.SetHostStatus(HostStatus.Unknown, null);
    }

    /// <summary>Checks one node again at once, after its host name, port or protocol changed.</summary>
    public Task CheckAsync(ConnectionNodeViewModel node) =>
        _running is { } running ? CheckAsync([node], running.Token) : Task.CompletedTask;

    /// <summary>One round over all nodes.</summary>
    internal Task CheckAllAsync() => _running is { } running ? CheckAsync(_nodes().ToList(), running.Token) : Task.CompletedTask;

    public void Dispose() => Stop();

    /// <summary>The host and port that tell whether a connection's server is up; null when there is none to check.</summary>
    internal static (string Host, int Port)? TargetOf(ConnectionInfo connection)
    {
        if (connection is ContainerInfo || connection.Protocol is ProtocolType.IntApp or ProtocolType.WSL or ProtocolType.AnyDesk)
            return null;
        // The server is only reachable through the tunnel's host; reaching it directly says nothing.
        if (!string.IsNullOrWhiteSpace(connection.SSHTunnelConnectionName))
            return null;
        if (connection.Protocol is ProtocolType.Terminal or ProtocolType.PowerShell && SessionPlanner.IsLocal(connection))
            return null;

        if (connection.Protocol is ProtocolType.HTTP or ProtocolType.HTTPS)
        {
            try
            {
                Uri address = WebAddress.For(connection);
                return (address.Host, address.Port);
            }
            catch (SessionException)
            {
                return null;
            }
        }

        string host = connection.Hostname.Trim();
        int port = connection.Port > 0 ? connection.Port : DefaultPorts.For(connection.Protocol);
        return host.Length == 0 || port is < 1 or > 65535 ? null : (host, port);
    }

    internal static async Task<string?> ProbeAsync(string host, int port, CancellationToken cancellation)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(CheckTimeout);
        using TcpClient client = new();
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return UiText.NoAnswerWithin(CheckTimeout);
        }
        catch (SocketException ex)
        {
            return ex.Message;
        }
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            using PeriodicTimer timer = new(_interval);
            do
            {
                await CheckAsync(_nodes().ToList(), cancellation);
            }
            while (await timer.WaitForNextTickAsync(cancellation));
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private async Task CheckAsync(IReadOnlyList<ConnectionNodeViewModel> nodes, CancellationToken cancellation)
    {
        List<IGrouping<(string Host, int Port)?, ConnectionNodeViewModel>> targets = nodes.GroupBy(node => TargetOf(node.Connection)).ToList();
        foreach (ConnectionNodeViewModel node in targets.Where(group => group.Key is null).SelectMany(group => group))
            node.SetHostStatus(HostStatus.Unknown, null);

        using SemaphoreSlim turns = new(MaxParallelChecks);
        try
        {
            await Task.WhenAll(targets.Where(group => group.Key is not null).Select(async group =>
            {
                (string host, int port) = group.Key!.Value;
                await turns.WaitAsync(cancellation);
                string? problem;
                try
                {
                    problem = await Probe(host, port, cancellation);
                }
                finally
                {
                    turns.Release();
                }

                // A node may have changed or the checks stopped meanwhile: only show what still applies.
                if (cancellation.IsCancellationRequested)
                    return;
                foreach (ConnectionNodeViewModel node in group.Where(node => TargetOf(node.Connection) == (host, port)))
                    node.SetHostStatus(problem is null ? HostStatus.Online : HostStatus.Offline, problem);
            }));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Stopped.
        }
    }
}
