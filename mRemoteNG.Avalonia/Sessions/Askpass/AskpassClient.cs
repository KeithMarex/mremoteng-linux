using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace mRemoteNG.Avalonia.Sessions.Askpass;

/// <summary>
/// The SSH_ASKPASS helper: what this app does when ssh starts it to answer a prompt (the prompt is the only argument).
/// It asks the session's <see cref="AskpassServer"/>; for any prompt the server leaves to the user it asks in the
/// terminal, which is the helper's controlling terminal, so the session looks as if ssh had asked itself.
/// </summary>
internal static class AskpassClient
{
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Whether this process was started by ssh as the askpass helper of a session.</summary>
    public static bool IsRequested(Func<string, string?> getEnvironmentVariable) =>
        !string.IsNullOrEmpty(getEnvironmentVariable(AskpassServer.SocketVariable));

    /// <summary>Answers the prompt on standard output; the exit code tells ssh whether there is an answer.</summary>
    public static int Run(string[] args, Func<string, string?> getEnvironmentVariable)
    {
        string prompt = args.Length > 0 ? args[0] : string.Empty;

        // OpenSSH 8.4+: "none" only shows a message (e.g. touch your security key); "confirm" wants yes or no.
        switch (getEnvironmentVariable("SSH_ASKPASS_PROMPT"))
        {
            case "none":
                TerminalPrompt.Show(prompt);
                return 0;
            case "confirm":
                return TerminalPrompt.Ask(prompt, echo: true)?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true ? 0 : 1;
        }

        string? answer = AskServer(getEnvironmentVariable, prompt) is { AskInTerminal: false, Password: { } password }
            ? password
            : TerminalPrompt.Ask(prompt, echo: IsQuestion(prompt));
        if (answer is null)
            return 1;

        using Stream output = Console.OpenStandardOutput();
        output.Write(Encoding.UTF8.GetBytes(answer + "\n"));
        return 0;
    }

    /// <summary>Asks the server; null when it cannot be reached (the app was closed), so the terminal is asked instead.</summary>
    internal static AskpassReply? AskServer(Func<string, string?> getEnvironmentVariable, string prompt)
    {
        string? socketPath = getEnvironmentVariable(AskpassServer.SocketVariable);
        if (string.IsNullOrEmpty(socketPath))
            return null;

        try
        {
            using CancellationTokenSource timeout = new(ServerTimeout);
            using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeout.Token).AsTask().GetAwaiter().GetResult();
            using NetworkStream stream = new(socket);
            AskpassRequest request = new(getEnvironmentVariable(AskpassServer.TokenVariable), prompt);
            AskpassProtocol.WriteLineAsync(stream, JsonSerializer.Serialize(request), timeout.Token).GetAwaiter().GetResult();
            string? line = AskpassProtocol.ReadLineAsync(stream, timeout.Token).GetAwaiter().GetResult();
            return line is null ? null : JsonSerializer.Deserialize<AskpassReply>(line);
        }
        catch (Exception ex) when (ex is IOException or SocketException or JsonException or OperationCanceledException
                                       or InvalidDataException)
        {
            return null;
        }
    }

    // ssh's host key confirmation ("... (yes/no/[fingerprint])?", "Please type 'yes' or 'no': ") is typed visibly;
    // everything else (passwords, passphrases, one-time codes) is not echoed.
    private static bool IsQuestion(string prompt) =>
        prompt.Contains("(yes/no", StringComparison.OrdinalIgnoreCase) || prompt.Contains("'yes'", StringComparison.Ordinal);
}

/// <summary>Asks a question on the controlling terminal (the session's pseudo-terminal).</summary>
internal static class TerminalPrompt
{
    private const string Terminal = "/dev/tty";

    public static void Show(string message)
    {
        try
        {
            using FileStream tty = new(Terminal, FileMode.Open, FileAccess.Write);
            Write(tty, message.TrimEnd() + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The line typed in answer, or null without a terminal or when the input ended.</summary>
    public static string? Ask(string prompt, bool echo)
    {
        try
        {
            using FileStream tty = new(Terminal, FileMode.Open, FileAccess.ReadWrite);
            Write(tty, prompt);
            if (!echo)
                SetEcho(false);
            try
            {
                return ReadLine(tty);
            }
            finally
            {
                if (!echo)
                {
                    SetEcho(true);
                    Write(tty, "\n"); // the Enter that ended the hidden input was not echoed either
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void Write(Stream tty, string text)
    {
        tty.Write(Encoding.UTF8.GetBytes(text));
        tty.Flush();
    }

    private static string? ReadLine(Stream tty)
    {
        List<byte> line = [];
        int value;
        while ((value = tty.ReadByte()) >= 0 && value != '\n')
            line.Add((byte)value);
        return value < 0 && line.Count == 0 ? null : Encoding.UTF8.GetString([.. line]).TrimEnd('\r');
    }

    private static void SetEcho(bool on)
    {
        using Process? stty = Process.Start(new ProcessStartInfo("stty", ["-F", Terminal, on ? "echo" : "-echo"])
        {
            UseShellExecute = false
        });
        stty?.WaitForExit();
    }
}
