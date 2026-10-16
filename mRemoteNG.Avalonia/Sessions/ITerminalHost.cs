namespace mRemoteNG.Avalonia.Sessions;

/// <summary>The terminal a session runs in; implemented by the session view, faked in tests.</summary>
public interface ITerminalHost
{
    /// <summary>Starts <paramref name="plan"/>'s program in the terminal's pseudo-terminal.</summary>
    Task StartAsync(SessionPlan plan, IReadOnlyDictionary<string, string> environment);

    /// <summary>Ends the running program.</summary>
    void Kill();

    /// <summary>Raised on the UI thread when the program ends, with its exit code when known.</summary>
    event EventHandler<int?>? Exited;
}
