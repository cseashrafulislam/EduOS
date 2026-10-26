using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    private static string HostelCommandFingerprint(string operation, long targetId, params object?[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Operation = operation, TargetId = targetId, Parts = parts })))).ToLowerInvariant();

    // Compatibility with clients deployed before ClientRequestId was available.
    // The original row version is included in the fingerprint, so retries cannot
    // alias another correction made to the same allocation in the future.
    private static Guid DeriveHostelCommandRequestId(long tenantId, long actorId,
        string operation, long allocationId, string requestHash)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"eduos:hostel-command:v1:{tenantId}:{actorId}:{operation}:{allocationId}:{requestHash}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}
