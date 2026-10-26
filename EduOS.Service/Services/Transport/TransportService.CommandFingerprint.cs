using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    private static string CommandFingerprint(string operation, long targetId, params object?[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Operation = operation, TargetId = targetId, Parts = parts })))).ToLowerInvariant();

    // Prior driver-command clients did not send request IDs. The original row version
    // is part of the fingerprint, so retries derive the same receipt without giving
    // unrelated later updates the same key. The hash domain below must stay unchanged:
    // receipts from the previously deployed extension endpoints depend on it.
    private static Guid DeriveDriverCommandRequestId(long tenantId, long actorId, string operation,
        long assignmentId, string requestHash)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"eduos:driver-extension:v1:{tenantId}:{actorId}:{operation}:{assignmentId}:{requestHash}"));
        return new Guid(digest.AsSpan(0, 16));
    }

    // Older route/vehicle UIs did not send a key. Preserve their request contract
    // while binding each retry to the original row version and exact payload.
    private static Guid DeriveRouteVehicleCommandRequestId(long tenantId, long actorId,
        string operation, long assignmentId, string requestHash)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"eduos:route-vehicle-command:v1:{tenantId}:{actorId}:{operation}:{assignmentId}:{requestHash}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}
