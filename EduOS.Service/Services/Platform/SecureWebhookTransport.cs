using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Platform;

/// <summary>One-shot outbound webhook transport. DNS is resolved and pinned inside ConnectCallback;
/// redirects and proxies are disabled, and response bodies are never consumed.</summary>
public static class SecureWebhookTransport
{
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false; // conservative: IPv6 egress disabled
        var b = address.GetAddressBytes();
        if (IPAddress.IsLoopback(address) || b[0] is 0 or 10 or 127 or 169 || b[0] >= 224) return false;
        if (b[0] == 100 && b[1] is >= 64 and <= 127) return false;
        if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;
        if (b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 88 && b[2] == 99)) return false;
        if (b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100)) return false;
        if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;
        if (b[0] == 192 && b[1] == 0 && b[2] == 2) return false;
        if (b[0] == 192 && b[1] == 31 && b[2] == 196) return false;
        if (b[0] == 192 && b[1] == 52 && b[2] == 193) return false;
        return true;
    }

    public static string Signature(string secret, string timestamp, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(timestamp + "." + payload);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<(bool Succeeded, string? Failure)> SendAsync(
        string destination, string payload, string secret, Guid eventId, CancellationToken cancellationToken)
    {
        if (!IntegrationAdministrationService.IsPublicWebhookUrl(destination))
            return (false, "Destination failed HTTPS policy.");
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(3), PooledConnectionLifetime = TimeSpan.Zero,
            ConnectCallback = async (context, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                var publicAddress = addresses.FirstOrDefault(IsPublicAddress);
                if (publicAddress == null) throw new InvalidOperationException("No permitted public destination.");
                var socket = new Socket(publicAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(publicAddress, context.DnsEndPoint.Port), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(HttpMethod.Post, destination);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation("X-EduOS-Event-Id", eventId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-EduOS-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-EduOS-Signature", Signature(secret, timestamp, payload));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode ? (true, null) : (false, "HTTP " + (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return (false, "Delivery timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or InvalidOperationException or System.Security.Authentication.AuthenticationException)
        { return (false, "Destination connection or TLS validation failed."); }
    }
}
