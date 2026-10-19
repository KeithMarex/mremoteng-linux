using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Connection;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>Where a SPICE connection's console comes from.</summary>
public enum SpiceSource
{
    /// <summary>A Proxmox VE virtual machine, through the Proxmox API and its SPICE proxy.</summary>
    Proxmox,

    /// <summary>QEMU's own SPICE port (libvirt, plain QEMU).</summary>
    Direct
}

/// <summary>The SPICE settings of a connection, kept in its mRemoteNG for Linux plugin properties.</summary>
public static class SpiceSettings
{
    public const string SourceKey = "mRx.SpiceSource";
    public const string NodeKey = "mRx.ProxmoxNode";
    public const string VmKey = "mRx.ProxmoxVm";

    /// <summary>QEMU's first SPICE port, the default of a direct connection.</summary>
    public const int DirectPort = 5900;

    public static SpiceSource Source(ConnectionInfo connection) =>
        Enum.TryParse(connection.GetPluginProperty(SourceKey), out SpiceSource source) && Enum.IsDefined(source) ? source : SpiceSource.Proxmox;

    public static string? Node(ConnectionInfo connection) => connection.GetPluginProperty(NodeKey) is { Length: > 0 } node ? node : null;

    public static string? Vm(ConnectionInfo connection) => connection.GetPluginProperty(VmKey) is { Length: > 0 } vm ? vm : null;
}

/// <summary>
/// Opens a SPICE console in remote-viewer (virt-viewer), in its own window: a Proxmox VE virtual machine's, which the
/// Proxmox API hands out as a one-time ticket for its SPICE proxy, or QEMU's own SPICE port. remote-viewer gets what
/// it needs in a connection file (".vv", as the Proxmox web interface downloads), readable by the user only and deleted
/// once it has been read, so no password goes on a command line.
/// </summary>
/// <param name="readTlsCertificate">Reads the Proxmox API's TLS certificate, so the user can be asked about it first.</param>
/// <param name="handler">Makes the HTTP handler for the API, which accepts the given certificate (and trusted ones).</param>
public sealed class SpiceConnector(
    SessionPrograms programs,
    string runtimeDirectory,
    IDialogService dialogs,
    CertificatePins pins,
    Func<string, int, CancellationToken, Task<ServerCertificate>> readTlsCertificate,
    Func<Func<X509Certificate2, bool>, HttpMessageHandler> handler)
{
    private const string PackageName = "virt-viewer";

    public SpiceConnector(SessionPrograms programs, string runtimeDirectory, IDialogService dialogs, CertificatePins pins)
        : this(programs, runtimeDirectory, dialogs, pins, RdpCertificateProbe.ReadTlsAsync, AcceptingHandler)
    {
    }

    /// <summary>Opens the console; the task ends with remote-viewer's exit code.</summary>
    /// <exception cref="SessionException">The console cannot be opened, with the reason.</exception>
    public async Task<(SessionPlan Plan, Task<int> Exited)> LaunchAsync(ConnectionInfo connection, CancellationToken cancellation)
    {
        string viewer = programs.Locate(programs.RemoteViewer, PackageName);
        IReadOnlyList<KeyValuePair<string, string>> settings = SpiceSettings.Source(connection) == SpiceSource.Proxmox
            ? await ProxmoxAsync(connection, cancellation)
            : Direct(connection);

        string file = WriteViewerFile(settings);
        SessionPlan plan = new(viewer, [file]) { Description = $"{Path.GetFileName(viewer)} ({connection.Name})" };
        Task<int> exited;
        try
        {
            exited = programs.StartDetached(plan);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            File.Delete(file);
            throw new SessionException(UiText.CouldNotStart(Path.GetFileName(viewer), ex.Message));
        }

        return (plan, DeleteWhenDoneAsync(file, exited));
    }

    /// <summary>QEMU's SPICE port, with the connection's SPICE password if it has one.</summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> Direct(ConnectionInfo connection)
    {
        List<KeyValuePair<string, string>> settings =
        [
            new("type", "spice"),
            new("host", SessionPlanner.Host(connection)),
            new("port", SessionPlanner.Port(connection).ToString(CultureInfo.InvariantCulture))
        ];
        if (!string.IsNullOrEmpty(connection.Password))
            settings.Add(new("password", KeyFileValue(connection.Password)));
        settings.Add(new("title", KeyFileValue(Title(connection))));
        return settings;
    }

    // The file is a GLib key file, where a backslash starts an escape and leading spaces are dropped.
    private static string KeyFileValue(string value)
    {
        string escaped = value.Replace("\\", "\\\\");
        return escaped.StartsWith(' ') ? "\\s" + escaped[1..] : escaped;
    }

    /// <summary>The ".vv" file remote-viewer reads, in the [virt-viewer] section; it deletes the file once read.</summary>
    internal static string ViewerFile(IEnumerable<KeyValuePair<string, string>> settings)
    {
        StringBuilder file = new("[virt-viewer]\n");
        foreach ((string key, string value) in settings)
        {
            // A connection file or the Proxmox API must not be able to add lines (settings) of its own.
            if (key.Length == 0 || !key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') || value.Any(char.IsControl))
                throw new SessionException(UiText.UnsafeSpiceValue);
            file.Append(key).Append('=').Append(value).Append('\n');
        }

        return file.Append("delete-this-file=1\n").ToString();
    }

    private async Task<IReadOnlyList<KeyValuePair<string, string>>> ProxmoxAsync(ConnectionInfo connection, CancellationToken cancellation)
    {
        // The console itself goes to Proxmox's SPICE proxy (port 3128), not to the API port the tunnel forwards.
        if (TunnelLease.ServerOf(connection) is not null)
            throw new SessionException(UiText.ProxmoxThroughSshTunnel);
        string vm = SpiceSettings.Vm(connection) ?? throw new SessionException(UiText.ProxmoxVmNotSet);
        string host = SessionPlanner.Host(connection);
        int port = SessionPlanner.Port(connection);

        X509Certificate2 accepted = await AcceptCertificateAsync(host, port, cancellation);
        using HttpClient client = new(handler(certificate => certificate.RawDataMemory.Span.SequenceEqual(accepted.RawDataMemory.Span)))
        {
            BaseAddress = new UriBuilder("https", host, port, "/api2/json/").Uri,
            Timeout = TimeSpan.FromSeconds(30)
        };

        try
        {
            await LogInAsync(client, connection, host, cancellation);
            (string node, int vmId) = await FindVmAsync(client, vm, SpiceSettings.Node(connection), cancellation);
            using JsonDocument ticket = await PostAsync(client, $"nodes/{Uri.EscapeDataString(node)}/qemu/{vmId}/spiceproxy",
                                                        [new("proxy", host)], cancellation);
            return ticket.RootElement.GetProperty("data").EnumerateObject()
                         .Where(setting => setting.Name != "delete-this-file")
                         // Written as the Proxmox web interface writes them: already escaped (its CA as one line with \n).
                         .Select(setting => new KeyValuePair<string, string>(setting.Name, Text(setting.Value).Replace("\n", "\\n")))
                         .ToList();
        }
        catch (HttpRequestException ex)
        {
            throw new SessionException(UiText.CouldNotConnectTo(host, port, ex.Message));
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new SessionException(UiText.CouldNotConnectTo(host, port, UiText.NoAnswerWithin(client.Timeout)));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new SessionException(UiText.ProxmoxUnexpectedAnswer);
        }
    }

    // As for RDP and FTPS: a certificate this computer does not trust is shown to the user, who may trust it.
    private async Task<X509Certificate2> AcceptCertificateAsync(string host, int port, CancellationToken cancellation)
    {
        ServerCertificate server;
        try
        {
            server = await readTlsCertificate(host, port, cancellation);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException or AuthenticationException or OperationCanceledException
                                       && !cancellation.IsCancellationRequested)
        {
            throw new SessionException(UiText.CouldNotConnectTo(host, port, ex.Message));
        }

        CertificateQuestion question = new(host, port, server.Certificate, Problems(server.Errors));
        if (server.Errors != SslPolicyErrors.None && !pins.IsTrusted(host, port, question.Fingerprint))
        {
            TrustDecision decision = await dialogs.AskTrustAsync(UiText.UntrustedCertificateTitle, UiText.UntrustedCertificate(question));
            if (decision == TrustDecision.Reject)
                throw new SessionException(UiText.CertificateRejected);
            if (decision == TrustDecision.TrustAndRemember)
                pins.Trust(host, port, question.Fingerprint);
        }

        return server.Certificate;
    }

    // An API token ("user@realm!tokenid", with its secret as the password) or a user's ticket.
    private async Task LogInAsync(HttpClient client, ConnectionInfo connection, string host, CancellationToken cancellation)
    {
        string user = connection.Username.Trim();
        string password = connection.Password ?? string.Empty;
        if (user.Contains('!') && password.Length > 0)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"PVEAPIToken={user}={password}");
            return;
        }

        if (user.Length == 0 || password.Length == 0)
        {
            LoginCredentials login = await dialogs.AskLoginAsync(UiText.LoginPrompt(host), new LoginCredentials(user, string.Empty, password))
                                     ?? throw new SessionException(UiText.ConnectionCancelled);
            (user, password) = (login.Username.Trim(), login.Password);
            if (user.Contains('!'))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"PVEAPIToken={user}={password}");
                return;
            }
        }

        // Without a realm, Proxmox's own default: the Linux users of the host (pam).
        using JsonDocument answer = await PostAsync(client, "access/ticket",
                                                    [new("username", user.Contains('@') ? user : user + "@pam"), new("password", password)],
                                                    cancellation, UiText.ProxmoxLoginFailed);
        JsonElement data = answer.RootElement.GetProperty("data");
        if (data.TryGetProperty("NeedTFA", out JsonElement twoFactor) && Text(twoFactor) is "1" or "true")
            throw new SessionException(UiText.ProxmoxTwoFactor);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "PVEAuthCookie=" + Uri.EscapeDataString(data.GetProperty("ticket").GetString()!));
        client.DefaultRequestHeaders.TryAddWithoutValidation("CSRFPreventionToken", data.GetProperty("CSRFPreventionToken").GetString());
    }

    // The VM by ID or name, and the node it is on (as set, or as the cluster says).
    private static async Task<(string Node, int VmId)> FindVmAsync(HttpClient client, string vm, string? node, CancellationToken cancellation)
    {
        bool byId = int.TryParse(vm, NumberStyles.None, CultureInfo.InvariantCulture, out int vmId);
        if (byId && node is not null)
            return (node, vmId);

        using JsonDocument resources = await GetAsync(client, "cluster/resources?type=vm", cancellation);
        foreach (JsonElement resource in resources.RootElement.GetProperty("data").EnumerateArray())
        {
            bool matches = byId
                ? resource.TryGetProperty("vmid", out JsonElement id) && Text(id) == vmId.ToString(CultureInfo.InvariantCulture)
                : resource.TryGetProperty("name", out JsonElement name) && name.GetString() == vm;
            if (!matches || (node is not null && resource.GetProperty("node").GetString() != node))
                continue;
            if (resource.GetProperty("type").GetString() != "qemu")
                throw new SessionException(UiText.ProxmoxContainer(vm));
            return (resource.GetProperty("node").GetString()!, int.Parse(Text(resource.GetProperty("vmid")), CultureInfo.InvariantCulture));
        }

        throw new SessionException(UiText.ProxmoxVmNotFound(vm));
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.GetAsync(path, cancellation);
        return await ReadAsync(response, null, cancellation);
    }

    private static async Task<JsonDocument> PostAsync(HttpClient client, string path, IEnumerable<KeyValuePair<string, string>> form,
                                                      CancellationToken cancellation, string? unauthorized = null)
    {
        using FormUrlEncodedContent content = new(form);
        using HttpResponseMessage response = await client.PostAsync(path, content, cancellation);
        return await ReadAsync(response, unauthorized, cancellation);
    }

    // Proxmox explains a refusal in the reason phrase ("VM 100 not running") or in the body's "message".
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, string? unauthorized, CancellationToken cancellation)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SessionException(unauthorized ?? UiText.ProxmoxNotAllowed);
        string body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            string? message = null;
            try
            {
                using JsonDocument error = JsonDocument.Parse(body);
                message = error.RootElement.TryGetProperty("message", out JsonElement text) ? text.GetString()?.Trim() : null;
            }
            catch (JsonException)
            {
            }

            throw new SessionException(UiText.ProxmoxRefused((int)response.StatusCode, message ?? response.ReasonPhrase));
        }

        return JsonDocument.Parse(body);
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static string Title(ConnectionInfo connection) => new(connection.Name.Where(c => !char.IsControl(c)).ToArray());

    private static string Problems(SslPolicyErrors errors)
    {
        List<string> problems = [];
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            problems.Add(UiText.CertificateNameMismatch);
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            problems.Add(UiText.CertificateNotTrusted);
        return string.Join("; ", problems);
    }

    private string WriteViewerFile(IEnumerable<KeyValuePair<string, string>> settings)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The viewer's file needs Unix permissions.");
        string text = ViewerFile(settings);
        Directory.CreateDirectory(runtimeDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string file = Path.Combine(runtimeDirectory, $"spice-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}.vv");
        using (FileStream stream = new(file, new FileStreamOptions
               {
                   Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
               }))
        {
            stream.Write(Encoding.UTF8.GetBytes(text));
        }

        return file;
    }

    // remote-viewer deletes the file once it has read it; this is for when it never got that far.
    private static async Task<int> DeleteWhenDoneAsync(string file, Task<int> exited)
    {
        try
        {
            return await exited;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static HttpMessageHandler AcceptingHandler(Func<X509Certificate2, bool> accept) => new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None || (certificate is not null && accept(new X509Certificate2(certificate)))
        }
    };
}
