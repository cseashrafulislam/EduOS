using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Platform;

/// <summary>
/// Durable successful command outcomes. The caller must hold a serializable transaction
/// over the canonical mutation and both SaveChanges calls. Never persist failed outcomes.
/// </summary>
public sealed class ReservationCommandReceiptStore(EduOSDbContext db, TimeProvider clock)
    : IReservationCommandReceiptStore
{
    public async Task<ReservationCommandReplay<T>> ReplayAsync<T>(long tenantId, long actorId,
        string scope, Guid requestId, long targetId, string requestHash, CancellationToken ct = default)
    {
        if (!Valid(tenantId, actorId, scope, requestId, targetId, requestHash))
            return new(true, true, null);
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Scope == scope && x.RequestId == requestId && !x.IsDeleted, ct);
        if (record == null) return new(false, false, null);
        if (record.TargetId != targetId || record.CompletedByUserId != actorId ||
            !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(record.ResultJson) ||
            !string.Equals(record.ResultHash, Hash(record.ResultJson), StringComparison.Ordinal))
            return new(true, true, null);
        try
        {
            var previous = JsonSerializer.Deserialize<ApiResponse<T>>(record.ResultJson);
            return previous?.Success == true
                ? new(true, false, previous) : new(true, true, null);
        }
        catch (JsonException) { return new(true, true, null); }
    }

    public Task RecordAsync<T>(long tenantId, long actorId, string scope, Guid requestId,
        long targetId, string requestHash, ApiResponse<T> response, CancellationToken ct = default)
    {
        if (!Valid(tenantId, actorId, scope, requestId, targetId, requestHash) || !response.Success)
            throw new InvalidOperationException("Invalid successful command receipt.");
        var resultJson = JsonSerializer.Serialize(response);
        if (resultJson.Length > 4000)
            throw new InvalidOperationException("Command receipt exceeds the supported outcome size.");
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            TenantId = tenantId, Scope = scope, RequestId = requestId, TargetId = targetId,
            RequestHash = requestHash, ResultHash = Hash(resultJson), ResultJson = resultJson,
            CompletedByUserId = actorId, CompletedAt = clock.GetUtcNow().UtcDateTime,
            ExpiresAt = DateTime.MaxValue
        });
        return Task.CompletedTask;
    }

    private static bool Valid(long tenantId, long actorId, string scope, Guid requestId,
        long targetId, string hash) => tenantId > 0 && actorId > 0 &&
        targetId > 0 && requestId != Guid.Empty && scope.Length is > 0 and <= 100 &&
        hash.Length == 64;

    public static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
