using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Platform;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using MimeKit;

namespace EduOS.Service.Services.Alumni;

public sealed record AlumniGatewaySendResult(bool Accepted, string? FailureCode);

public interface IAlumniInvitationTransport
{
    Task<AlumniGatewaySendResult> SendAsync(CommunicationGateway gateway, string destination,
        string subject, string text, CancellationToken ct);
}

/// <summary>
/// Tenant-selected SMTP or compatible HTTPS JSON gateway. Credentials are unprotected
/// in memory only for the actual attempt. Public DNS is pinned to the connection,
/// redirects/proxies are disabled, and no destination, message or secret is logged.
/// Non-2xx/timeout is an ambiguous attempt, never automatically retried.
/// </summary>
public sealed class AlumniInvitationTransport : IAlumniInvitationTransport
{
    private readonly IDataProtector _protector;

    public AlumniInvitationTransport(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("EduOS.Communication.GatewayCredentials.v1");

    public async Task<AlumniGatewaySendResult> SendAsync(CommunicationGateway gateway,
        string destination, string subject, string text, CancellationToken ct)
    {
        if (!gateway.IsActive || !gateway.IsDefault || gateway.IsDeleted ||
            gateway.Channel is not (NotificationChannelType.Email or NotificationChannelType.Sms) ||
            string.IsNullOrWhiteSpace(gateway.ProtectedCredential))
            return new(false, "GATEWAY_DISABLED");

        string credential;
        try { credential = _protector.Unprotect(gateway.ProtectedCredential); }
        catch (CryptographicException) { return new(false, "GATEWAY_SECRET_UNAVAILABLE"); }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            if (gateway.Channel == NotificationChannelType.Email && gateway.ProviderCode == "SMTP")
                return await SendSmtpAsync(gateway, destination, subject, text, credential, timeout.Token);
            return await SendJsonAsync(gateway, destination, subject, text, credential, timeout.Token);
        }
        catch (OperationCanceledException) { return new(false, "OUTCOME_UNCERTAIN_TIMEOUT"); }
        catch (Exception) { return new(false, "OUTCOME_UNCERTAIN_TRANSPORT"); }
    }

    private static async Task<AlumniGatewaySendResult> SendSmtpAsync(CommunicationGateway gateway,
        string destination, string subject, string text, string password, CancellationToken ct)
    {
        var host = gateway.SmtpHost;
        if (string.IsNullOrWhiteSpace(host) || gateway.SmtpPort is not (465 or 587 or 2525) ||
            string.IsNullOrWhiteSpace(gateway.SmtpUsername) || string.IsNullOrWhiteSpace(gateway.FromEmail) ||
            !gateway.UseSsl || string.IsNullOrWhiteSpace(destination))
            return new(false, "GATEWAY_INVALID");

        // MailKit receives the already connected socket, retaining hostname-based
        // certificate verification while preventing a second DNS lookup/rebinding.
        using var socket = await ConnectPublicAsync(host, gateway.SmtpPort.Value, ct);
        using var client = new MailKit.Net.Smtp.SmtpClient();
        var options = gateway.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(socket, host, gateway.SmtpPort.Value, options, ct);
        await client.AuthenticateAsync(gateway.SmtpUsername, password, ct);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(gateway.FromName ?? "", gateway.FromEmail));
        message.To.Add(MailboxAddress.Parse(destination));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };
        await client.SendAsync(message, ct);
        // Accepted by SMTP relay, NOT proof of delivery to an inbox.
        return new(true, null);
    }

    private static async Task<AlumniGatewaySendResult> SendJsonAsync(CommunicationGateway gateway,
        string destination, string subject, string text, string credential, CancellationToken ct)
    {
        if (!Uri.TryCreate(gateway.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            string.IsNullOrEmpty(endpoint.Host) ||
            (gateway.Channel == NotificationChannelType.Sms && string.IsNullOrWhiteSpace(gateway.SmsSenderId)))
            return new(false, "GATEWAY_INVALID");

        // Contract: POST JSON with Bearer API key; 2xx means provider accepted.
        // SMS: {to,from,text}; Email: {to,from,fromName,subject,text}.
        // No vendor-specific field names, credentials, domains or phone numbers in code.
        var payload = gateway.Channel == NotificationChannelType.Sms
            ? JsonSerializer.Serialize(new { to = destination, from = gateway.SmsSenderId, text })
            : JsonSerializer.Serialize(new { to = destination, from = gateway.FromEmail,
                fromName = gateway.FromName, subject, text });
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(3), PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = async (context, cancellation) =>
            {
                var socket = await ConnectPublicAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellation);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return response.IsSuccessStatusCode
            ? new(true, null) : new(false, "PROVIDER_HTTP_" + (int)response.StatusCode);
    }

    private static async Task<Socket> ConnectPublicAsync(string host, int port, CancellationToken ct)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Private gateway destination.");
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        var publicAddress = addresses.FirstOrDefault(SecureWebhookTransport.IsPublicAddress);
        if (publicAddress == null) throw new InvalidOperationException("No permitted public gateway address.");
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(publicAddress, port), ct);
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }
}
