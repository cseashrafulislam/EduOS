using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    private static string CommandFingerprint(string operation, long targetId, params object?[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Operation = operation, TargetId = targetId, Parts = parts })))).ToLowerInvariant();

    // Prior driver-extension clients did not send request IDs. The original row version
    // is part of the fingerprint, so retries derive the same receipt without giving
    // unrelated later updates the same key. Keep this fallback only for legacy calls.
    private static Guid LegacyExtensionRequestId(long tenantId, long actorId, string operation,
        long assignmentId, string requestHash)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"eduos:driver-extension:v1:{tenantId}:{actorId}:{operation}:{assignmentId}:{requestHash}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}
