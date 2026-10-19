namespace mRemoteNG.Avalonia.Sessions;

/// <summary>What opening a connection runs in the terminal's pseudo-terminal: ssh, telnet, a shell or the raw TCP relay.</summary>
public sealed record SessionPlan(string Program, IReadOnlyList<string> Arguments)
{
    /// <summary>Settings of the connection that could not be applied; they go to the notifications.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The connection's stored password, which the askpass helper hands to ssh once.</summary>
    public string? Password { get; init; }

    /// <summary>The host the password belongs to; a password prompt naming another host (a jump host) does not get it.</summary>
    public string? PasswordHost { get; init; }

    /// <summary>What the session is, when the command line would not tell the user (the raw TCP relay).</summary>
    public string? Description { get; init; }

    /// <summary>Where the program runs; null for the user's home folder.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>The command line (or description) as shown to the user; it never holds a password.</summary>
    public override string ToString() => Description ?? string.Join(' ', [Path.GetFileName(Program), .. Arguments]);
}

/// <summary>A connection cannot be opened; the message is shown to the user.</summary>
public sealed class SessionException(string message) : Exception(message);
