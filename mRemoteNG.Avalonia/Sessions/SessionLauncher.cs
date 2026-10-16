using System.Net.Sockets;
using mRemoteNG.Avalonia.Sessions.Askpass;
using mRemoteNG.Connection;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>Prepares a connection for a terminal: what to run, plus the askpass helper when a password is stored.</summary>
/// <param name="programs">The programs sessions run.</param>
/// <param name="runtimeDirectory">Where askpass sockets go.</param>
public sealed class SessionLauncher(SessionPrograms programs, string runtimeDirectory)
{
    private readonly SessionPlanner _planner = new(programs);

    public SessionPrograms Programs { get; } = programs;

    /// <exception cref="SessionException">The connection cannot be opened.</exception>
    public PreparedSession Prepare(ConnectionInfo connection)
    {
        SessionPlan plan = _planner.Plan(connection);
        if (plan.Password is not { } password)
            return new PreparedSession(plan, new Dictionary<string, string>(), plan.Warnings, null);

        List<string> warnings = [.. plan.Warnings];
        if (Programs.App is not { } helper)
        {
            warnings.Add(UiText.StoredPasswordUnavailable);
            return new PreparedSession(plan, new Dictionary<string, string>(), warnings, null);
        }

        try
        {
            AskpassServer server = AskpassServer.Start(runtimeDirectory, helper, password, plan.PasswordHost);
            return new PreparedSession(plan, server.Environment, warnings, server);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException or ArgumentException)
        {
            warnings.Add($"{UiText.StoredPasswordUnavailable} ({ex.Message})");
            return new PreparedSession(plan, new Dictionary<string, string>(), warnings, null);
        }
    }
}

/// <summary>A connection ready to start; disposing it stops its askpass server.</summary>
public sealed class PreparedSession(SessionPlan plan, IReadOnlyDictionary<string, string> environment,
                                    IReadOnlyList<string> warnings, IDisposable? resources) : IDisposable
{
    public SessionPlan Plan { get; } = plan;

    /// <summary>Extra environment variables for the program; merged into the app's own.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; } = environment;

    public IReadOnlyList<string> Warnings { get; } = warnings;

    public void Dispose() => resources?.Dispose();
}
