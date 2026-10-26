using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    private static string CommandFingerprint(string operation, long targetId, params object?[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Operation = operation, TargetId = targetId, Parts = parts })))).ToLowerInvariant();
}
