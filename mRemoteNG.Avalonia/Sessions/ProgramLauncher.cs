using System.ComponentModel;
using System.Diagnostics;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// Opens connections whose program shows its own window, with no tab in this app: HTTP and HTTPS in the web browser,
/// AnyDesk in the AnyDesk client.
/// </summary>
public sealed class ProgramLauncher(SessionPrograms programs)
{
    /// <exception cref="SessionException">The connection cannot be opened, with the reason.</exception>
    public SessionPlan Plan(ConnectionInfo connection) => connection.Protocol switch
    {
        ProtocolType.HTTP or ProtocolType.HTTPS =>
            new SessionPlan(programs.Locate(programs.Browser, "xdg-utils"), [WebAddress.For(connection).AbsoluteUri]),
        ProtocolType.AnyDesk => AnyDesk(connection),
        _ => throw new SessionException(ProtocolRouting.NotSupportedReason(connection.Protocol))
    };

    /// <summary>Starts the connection's program; the task ends with its exit code.</summary>
    /// <exception cref="SessionException">The connection cannot be opened, with the reason.</exception>
    public (SessionPlan Plan, Task<int> Exited) Launch(ConnectionInfo connection)
    {
        SessionPlan plan = Plan(connection);
        try
        {
            return (plan, programs.StartDetached(plan));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            throw new SessionException(UiText.CouldNotStart(Path.GetFileName(plan.Program), ex.Message));
        }
    }

    // As on Windows: the AnyDesk ID (or alias) is the host name, and a stored password goes to AnyDesk's stdin.
    private SessionPlan AnyDesk(ConnectionInfo connection)
    {
        string id = connection.Hostname.Trim();
        if (id.Length == 0)
            throw new SessionException(Language.ConnectionOpenFailedNoHostname);
        if (!IsAnyDeskId(id))
            throw new SessionException(UiText.InvalidAnyDeskId);

        string anydesk = programs.Locate(programs.AnyDesk, null);
        bool withPassword = !string.IsNullOrEmpty(connection.Password);
        return new SessionPlan(anydesk, withPassword ? [id, "--with-password"] : [id])
        {
            Password = withPassword ? connection.Password : null
        };
    }

    /// <summary>An AnyDesk ID or alias: letters, digits, '@', '-', '_' and '.', and not an option.</summary>
    internal static bool IsAnyDeskId(string id) =>
        CommandLineValue.IsSafe(id) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '@' or '-' or '_' or '.');

    /// <summary>
    /// Starts a program on its own, with a <see cref="SessionPlan.Password"/> written to its stdin. The task ends with
    /// its exit code.
    /// </summary>
    internal static Task<int> Start(SessionPlan plan)
    {
        ProcessStartInfo info = new(plan.Program)
        {
            UseShellExecute = false,
            RedirectStandardInput = plan.Password is not null,
            WorkingDirectory = plan.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (string argument in plan.Arguments)
            info.ArgumentList.Add(argument);

        Process process = Process.Start(info) ?? throw new InvalidOperationException(plan.Program);
        if (plan.Password is { } password)
        {
            try
            {
                process.StandardInput.WriteLine(password);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The program ended or does not read its input; its exit code tells.
            }
        }

        return WaitAsync(process);
    }

    private static async Task<int> WaitAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }
}

/// <summary>The address an HTTP or HTTPS connection opens, built as the Windows app does.</summary>
internal static class WebAddress
{
    /// <exception cref="SessionException">The host name does not make a web address.</exception>
    public static Uri For(ConnectionInfo connection)
    {
        string address = connection.Hostname.Trim();
        if (address.Length == 0)
            throw new SessionException(Language.ConnectionOpenFailedNoHostname);

        // The host name may hold a whole address ("https://intranet/app"); otherwise the protocol gives the scheme.
        if (!address.Contains("://", StringComparison.Ordinal))
            address = (connection.Protocol == ProtocolType.HTTPS ? "https://" : "http://") + address;

        // Connection files are shared: only web addresses go to the browser, never file: or other schemes.
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") ||
            uri.Host.Length == 0)
        {
            throw new SessionException(UiText.InvalidWebAddress);
        }

        // A port in the address wins; otherwise the connection's port, when it is not the protocol's default.
        int port = connection.Port;
        if (!uri.IsDefaultPort || port <= 0 || port == DefaultPorts.For(connection.Protocol))
            return uri;
        if (port > 65535)
            throw new SessionException(UiText.InvalidPort(port));
        return new UriBuilder(uri) { Port = port }.Uri;
    }
}
