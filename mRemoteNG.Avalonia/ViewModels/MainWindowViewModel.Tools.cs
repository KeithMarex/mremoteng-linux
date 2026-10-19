using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Xml;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.ExternalTools;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>
/// The Tools menu (Options, External Tools), and running external tools: on a connection from its right-click menu,
/// as an External Tool connection (IntApp), and before and after a connection (PreExtApp and PostExtApp), as the
/// Windows app does.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private ExternalToolLauncher _tools = null!;

    /// <summary>The external tools, kept in extApps.xml in the config folder.</summary>
    public ObservableCollection<ExternalToolDefinition> ExternalTools { get; } = [];

    [RelayCommand]
    private async Task OpenOptionsAsync()
    {
        OptionsViewModel options = new(_settings);
        if (!await _dialogs.ShowOptionsAsync(options))
            return;

        options.ApplyTo(_settings);
        ApplyTheme(_settings.Theme);
        SaveSettings();
    }

    [RelayCommand]
    private async Task OpenExternalToolsAsync()
    {
        ExternalToolsViewModel editor = new(ExternalTools);
        if (!await _dialogs.ShowExternalToolsAsync(editor))
            return;

        ExternalTools.Clear();
        foreach (ExternalToolDefinition tool in editor.Result)
            ExternalTools.Add(tool);
        try
        {
            ExternalToolsFile.Save(_paths.ExternalToolsFile, ExternalTools);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ReportErrorAsync(UiText.CouldNotSaveExternalTools(_paths.ExternalToolsFile), ex);
        }
    }

    /// <summary>Runs a tool on the selected connection, or on every connection in the selected folder.</summary>
    [RelayCommand]
    private async Task RunExternalToolAsync(ExternalToolDefinition tool)
    {
        if (SelectedNode is not { } node)
            return;
        foreach (ConnectionInfo connection in ConnectionsIn(node.Connection))
            await RunToolAsync(tool, connection, waitForExit: false, message => _dialogs.ShowErrorAsync(UiText.ExternalTools, message));
    }

    private void LoadExternalTools(SessionPrograms programs)
    {
        _tools = new ExternalToolLauncher(programs);
        PropertyPanel.ExternalToolNames = () => ExternalTools.Select(tool => tool.DisplayName).ToList();
        try
        {
            foreach (ExternalToolDefinition tool in ExternalToolsFile.Load(_paths.ExternalToolsFile))
                ExternalTools.Add(tool);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            CoreRuntime.MessageCollector.AddExceptionMessage(UiText.CouldNotLoadExternalTools(_paths.ExternalToolsFile), ex);
        }
    }

    private static IEnumerable<ConnectionInfo> ConnectionsIn(ConnectionInfo node) =>
        node is ContainerInfo folder ? folder.Children.SelectMany(ConnectionsIn) : [node];

    private ExternalToolDefinition? FindTool(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : ExternalTools.FirstOrDefault(tool => string.Equals(tool.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>An External Tool connection (IntApp) runs the tool it names.</summary>
    private async Task OpenExternalToolConnectionAsync(ConnectionInfo connection)
    {
        if (string.IsNullOrWhiteSpace(connection.ExtApp))
            await ReportConnectErrorAsync(connection, UiText.ExternalToolNotSet);
        else if (FindTool(connection.ExtApp) is not { } tool)
            await ReportConnectErrorAsync(connection, UiText.ExternalToolNotFound(connection.ExtApp.Trim()));
        else if (!SshTunnels.Uses(connection))
            await RunToolAsync(tool, connection, waitForExit: false, message => ReportConnectErrorAsync(connection, message));
        else
        {
            // As on Windows, %HOSTNAME% and %PORT% are then the tunnel's local end.
            TunnelLease tunnel;
            try
            {
                tunnel = await Tunnels.OpenAsync(connection, CancellationToken.None);
            }
            catch (SessionException ex)
            {
                await ReportConnectErrorAsync(connection, ex.Message);
                return;
            }

            await RunToolAsync(tool, connection, waitForExit: false, message => ReportConnectErrorAsync(connection, message), tunnel);
        }
    }

    /// <summary>The connection's PreExtApp, before it connects; waited for when the tool says so.</summary>
    private async Task RunPreConnectionToolAsync(ConnectionInfo connection)
    {
        if (string.IsNullOrWhiteSpace(connection.PreExtApp))
            return;
        if (FindTool(connection.PreExtApp) is { } tool)
            await RunToolAsync(tool, connection, waitForExit: true, ReportToolProblem(connection));
        else
            await ReportToolProblem(connection)(UiText.ExternalToolNotFound(connection.PreExtApp.Trim()));
    }

    /// <summary>The connection's PostExtApp, once <paramref name="session"/> (opened for it) ends.</summary>
    private void RunPostConnectionToolWhenEnded(ConnectionInfo connection, SessionTabViewModel session)
    {
        if (string.IsNullOrWhiteSpace(connection.PostExtApp))
            return;

        void OnChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Remove || e.OldItems?.Contains(session) != true)
                return;
            connection.OpenConnections.CollectionChanged -= OnChanged;
            if (FindTool(connection.PostExtApp) is { } tool)
                _ = RunToolAsync(tool, connection, waitForExit: false, ReportToolProblem(connection));
            else
                _ = ReportToolProblem(connection)(UiText.ExternalToolNotFound(connection.PostExtApp.Trim()));
        }

        connection.OpenConnections.CollectionChanged += OnChanged;
    }

    private static Func<string, Task> ReportToolProblem(ConnectionInfo connection) => message =>
    {
        CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{connection.Name}: {message}");
        return Task.CompletedTask;
    };

    /// <summary>
    /// Runs <paramref name="tool"/> for <paramref name="connection"/>: in a terminal tab when it is integrated, else as
    /// a program of its own, waited for when <paramref name="waitForExit"/> and the tool both say so. Through
    /// <paramref name="tunnel"/>, the tool gets the tunnel's local end, and the tunnel closes when it is done.
    /// </summary>
    private async Task RunToolAsync(ExternalToolDefinition tool, ConnectionInfo connection, bool waitForExit, Func<string, Task> reportError,
                                    TunnelLease? tunnel = null)
    {
        SessionPlan plan;
        try
        {
            plan = _tools.Plan(tool, tunnel?.Target ?? connection);
        }
        catch (SessionException ex)
        {
            tunnel?.Dispose();
            await reportError(ex.Message);
            return;
        }

        foreach (string warning in plan.Warnings)
            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{connection.Name}: {warning}");
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, UiText.RunningTool(tool.DisplayName, connection.Name, plan.ToString()));
        if (tool.TryIntegrate)
        {
            SessionViewModel session = Sessions.OpenProgram(connection, plan);
            if (tunnel is not null)
                session.AdoptTunnel(tunnel);
            return;
        }

        Task<int> exited;
        try
        {
            exited = _tools.Start(plan);
        }
        catch (SessionException ex)
        {
            tunnel?.Dispose();
            await reportError(ex.Message);
            return;
        }

        Task logged = LogExitAsync(tool, connection, exited, tunnel);
        if (waitForExit && tool.WaitForExit)
            await logged;
    }

    private static async Task LogExitAsync(ExternalToolDefinition tool, ConnectionInfo connection, Task<int> exited, TunnelLease? tunnel)
    {
        int exitCode;
        try
        {
            exitCode = await exited;
        }
        finally
        {
            tunnel?.Dispose();
        }

        if (exitCode != 0)
            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{connection.Name}: {UiText.ProgramEnded(tool.DisplayName, exitCode)}");
    }
}
