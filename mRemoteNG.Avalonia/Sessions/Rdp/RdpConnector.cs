using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.Avalonia.Sessions.Rdp;

/// <summary>A started RDP session: what runs, and the program.</summary>
public sealed record RdpSession(FreeRdpPlan Plan, IRdpProcess Process);

/// <summary>
/// Opens an RDP connection: asks for a missing login, checks the server's certificate as the connection's
/// authentication level says, and starts xfreerdp.
/// </summary>
/// <param name="readCertificate">Reads an RDP server's certificate (host, port); null when it has none.</param>
/// <param name="readTlsCertificate">Reads a TLS server's certificate, for the gateway.</param>
/// <param name="start">Starts xfreerdp.</param>
public sealed class RdpConnector(
    SessionPrograms programs,
    IDialogService dialogs,
    CertificatePins pins,
    Func<string, int, CancellationToken, Task<ServerCertificate?>> readCertificate,
    Func<string, int, CancellationToken, Task<ServerCertificate>> readTlsCertificate,
    Func<FreeRdpPlan, IRdpProcess> start)
{
    private const int GatewayPort = 443;

    public RdpConnector(SessionPrograms programs, IDialogService dialogs, CertificatePins pins)
        : this(programs, dialogs, pins, RdpCertificateProbe.ReadRdpAsync, RdpCertificateProbe.ReadTlsAsync, FreeRdpProcess.Start)
    {
    }

    /// <exception cref="SessionException">The connection cannot be opened, with the reason.</exception>
    public async Task<RdpSession> ConnectAsync(ConnectionInfo connection, RdpDisplay display, CancellationToken cancellation)
    {
        string program = FreeRdpProgram.Locate(programs);
        string host = SessionPlanner.Host(connection);
        int port = SessionPlanner.Port(connection);
        LoginCredentials login = await LoginAsync(connection, host);
        List<string> warnings = [];
        string certificate = await CertificateOptionAsync(connection, host, port, warnings, cancellation);

        FreeRdpPlan plan = FreeRdpCommand.Build(connection, display, login, certificate, program);
        plan = plan with { Warnings = [.. warnings, .. plan.Warnings] };
        return new RdpSession(plan, start(plan));
    }

    // With CredSSP (Network Level Authentication) the login is needed before the session starts, so a missing one is
    // asked for, as Windows asks; without it the server shows its own logon screen.
    private async Task<LoginCredentials> LoginAsync(ConnectionInfo connection, string host)
    {
        LoginCredentials stored = new(connection.Username.Trim(), connection.Domain.Trim(), connection.Password);
        if (!connection.UseCredSsp || (stored.Username.Length > 0 && stored.Password.Length > 0))
            return stored;
        return await dialogs.AskLoginAsync(UiText.RdpLoginPrompt(host), stored)
               ?? throw new SessionException(UiText.ConnectionCancelled);
    }

    // Like the Windows app's AuthenticationLevel: NoAuth connects whatever the certificate, AuthRequired only with a
    // certificate this computer trusts, and WarnOnFailedAuth asks about any other certificate.
    private async Task<string> CertificateOptionAsync(ConnectionInfo connection, string host, int port, List<string> warnings,
                                                      CancellationToken cancellation)
    {
        switch (connection.RDPAuthenticationLevel)
        {
            case Connection.Protocol.RDP.AuthenticationLevel.NoAuth:
                return "ignore";
            case Connection.Protocol.RDP.AuthenticationLevel.AuthRequired:
                return "deny";
        }

        List<string> accepted = [];
        bool viaGateway = connection.RDGatewayUsageMethod != RDGatewayUsageMethod.Never && connection.RDGatewayHostname.Trim().Length > 0;
        if (viaGateway)
        {
            string gateway = connection.RDGatewayHostname.Trim();
            ServerCertificate? gatewayCertificate = await TryReadAsync(async () => await readTlsCertificate(gateway, GatewayPort, cancellation), gateway, warnings);
            if (gatewayCertificate is not null)
                accepted.Add(await AcceptAsync(gatewayCertificate, gateway, GatewayPort));
        }

        // Behind a gateway the computer may not be reachable directly; xfreerdp then trusts its certificate on first use.
        ServerCertificate? serverCertificate = connection.UseVmId
            ? null
            : await TryReadAsync(() => readCertificate(host, port, cancellation), host, viaGateway ? null : warnings);
        if (serverCertificate is not null)
            accepted.Add(await AcceptAsync(serverCertificate, host, port));
        else if (viaGateway)
            accepted.Add("tofu");

        // Without a certificate to accept (standard RDP security, or it could not be read), xfreerdp checks it itself and
        // refuses one this computer does not trust.
        return accepted.Count == 0 ? "deny" : string.Join(',', accepted);
    }

    private static async Task<ServerCertificate?> TryReadAsync(Func<Task<ServerCertificate?>> read, string host, List<string>? warnings)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or OperationCanceledException)
        {
            warnings?.Add(UiText.CertificateNotRead(host, ex.Message));
            return null;
        }
    }

    private async Task<string> AcceptAsync(ServerCertificate server, string host, int port)
    {
        CertificateQuestion question = new(host, port, server.Certificate, Problems(server.Errors));
        string fingerprint = Convert.ToHexString(server.Certificate.GetCertHash(HashAlgorithmName.SHA256));
        if (server.Errors != SslPolicyErrors.None && !pins.IsTrusted(host, port, question.Fingerprint))
        {
            TrustDecision decision = await dialogs.AskTrustAsync(UiText.UntrustedCertificateTitle, UiText.UntrustedCertificate(question));
            if (decision == TrustDecision.Reject)
                throw new SessionException(UiText.CertificateRejected);
            if (decision == TrustDecision.TrustAndRemember)
                pins.Trust(host, port, question.Fingerprint);
        }

        return "fingerprint:sha256:" + fingerprint;
    }

    private static string Problems(SslPolicyErrors errors)
    {
        List<string> problems = [];
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            problems.Add(UiText.CertificateNameMismatch);
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
            problems.Add(UiText.CertificateNotTrusted);
        return string.Join("; ", problems);
    }
}
