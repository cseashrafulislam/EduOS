using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using System.Security.Claims;

namespace EduOS.App.Security;

/// <summary>
/// Server-side cookie-session registry backed by the existing AspNetUserTokens table.
/// Separate provider namespace; no new schema or reuse of JWT refresh tokens.
/// </summary>
public static class AuthSessionRegistry
{
    public const string Provider = "EduOS.CookieSession";
    public const string ClaimType = "eduos_sid";

    public sealed class SessionRecord
    {
        public long? TenantId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string IpAddress { get; set; } = "";
        public string UserAgent { get; set; } = "";
    }

    public static async Task<string> CreateAsync(EduOSDbContext db, long userId, long? tenantId,
        string ip, string userAgent, bool rememberMe)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var sid = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var now = DateTime.UtcNow;
        var record = new SessionRecord
        {
            TenantId = tenantId, CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(rememberMe ? 30 : 1),
            IpAddress = Truncate(ip, 64), UserAgent = Truncate(userAgent, 300)
        };
        // Bound per-user session storage: remove expired/malformed entries and
        // evict the oldest sessions. This avoids unbounded tokens for active users.
        var existing = await db.UserTokens.Where(x =>
            x.UserId == userId && x.LoginProvider == Provider).ToListAsync();
        var retained = existing.Select(x => new { Token = x, Parsed = Parse(x.Value) })
            .Where(x => x.Parsed != null && x.Parsed.ExpiresAtUtc > now)
            .OrderByDescending(x => x.Parsed!.CreatedAtUtc).Take(19)
            .Select(x => x.Token).ToHashSet();
        db.UserTokens.RemoveRange(existing.Where(x => !retained.Contains(x)));
        db.UserTokens.Add(new IdentityUserToken<long>
        {
            UserId = userId, LoginProvider = Provider, Name = sid,
            Value = JsonSerializer.Serialize(record)
        });
        await db.SaveChangesAsync();
        return sid;
    }

    /// <summary>
    /// Exchange a tenant-bound session ID atomically. Concurrent requests using
    /// the same old ID cannot both succeed: the second delete encounters EF's
    /// affected-row concurrency check and the transaction rolls back.
    /// </summary>
    public static Task<string?> RotateAsync(EduOSDbContext db, long userId,
        string? currentSid, long fromTenantId, long toTenantId, string ip,
        string userAgent, bool rememberMe, CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync<string?>(async ct =>
        {
            if (userId <= 0 || fromTenantId <= 0 || toTenantId <= 0 ||
                fromTenantId == toTenantId || !ValidSid(currentSid))
                return null;

            var current = await db.UserTokens.SingleOrDefaultAsync(x =>
                x.UserId == userId && x.LoginProvider == Provider && x.Name == currentSid, ct);
            var record = Parse(current?.Value);
            var now = DateTime.UtcNow;
            if (current == null || record == null || record.TenantId != fromTenantId ||
                record.ExpiresAtUtc <= now)
                return null;

            var newSid = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            db.UserTokens.Remove(current);
            db.UserTokens.Add(new IdentityUserToken<long>
            {
                UserId = userId, LoginProvider = Provider, Name = newSid,
                Value = JsonSerializer.Serialize(new SessionRecord
                {
                    TenantId = toTenantId, CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(rememberMe ? 30 : 1),
                    IpAddress = Truncate(ip, 64), UserAgent = Truncate(userAgent, 300)
                })
            });
            await db.SaveChangesAsync(ct);
            return newSid;
        }, cancellationToken);

    // Revalidate the exact authenticated account and institution after sensitive reads
    // and inside serialized account writes; a middleware check alone cannot protect
    // a request whose session is revoked while SQL work is in flight.
    public static Task<bool> IsValidPrincipalAsync(EduOSDbContext db, ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Task.FromResult(false);

        var tenantClaim = principal.FindFirstValue("TenantId");
        long? tenantId = null;
        if (tenantClaim != null)
        {
            if (!long.TryParse(tenantClaim, out var parsed) || parsed <= 0)
                return Task.FromResult(false);
            tenantId = parsed;
        }
        return IsValidAsync(db, userId, principal.FindFirstValue(ClaimType), tenantId);
    }

    public static async Task<bool> IsValidAsync(EduOSDbContext db, long userId, string? sid, long? tenantId)
    {
        if (userId <= 0 || !ValidSid(sid)) return false;
        var value = await db.UserTokens.AsNoTracking()
            .Where(x => x.UserId == userId && x.LoginProvider == Provider && x.Name == sid)
            .Select(x => x.Value).SingleOrDefaultAsync();
        var record = Parse(value);
        return record != null && record.TenantId == tenantId && record.ExpiresAtUtc > DateTime.UtcNow;
    }

    public static async Task<IReadOnlyList<object>> ListAsync(EduOSDbContext db, long userId, string? currentSid)
    {
        var tokens = await db.UserTokens.AsNoTracking()
            .Where(x => x.UserId == userId && x.LoginProvider == Provider)
            .Select(x => new { x.Name, x.Value }).Take(500).ToListAsync();
        return tokens.Select(x => new { x.Name, Record = Parse(x.Value) })
            .Where(x => x.Record != null && x.Record.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.Record!.CreatedAtUtc).Take(100)
            .Select(x => (object)new
            {
                sessionId = ToPublicReference(x.Name), isCurrent = x.Name == currentSid,
                tenantId = x.Record!.TenantId, createdAtUtc = x.Record.CreatedAtUtc,
                expiresAtUtc = x.Record.ExpiresAtUtc, ipAddress = x.Record.IpAddress,
                userAgent = x.Record.UserAgent
            }).ToArray();
    }

    // Expose a non-reversible management reference, never a live cookie SID.
    public static string ToPublicReference(string sid) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("EduOS.session.management.v1:" + sid)));

    public static bool IsCurrentReference(string? currentSid, string? reference) =>
        ValidSid(currentSid) && ValidReference(reference) &&
        string.Equals(ToPublicReference(currentSid!), reference, StringComparison.Ordinal);

    /// <summary>
    /// Revoke a device only while the requesting cookie still belongs to this
    /// account and institution. The serializable token read and deletion share
    /// one transaction so a delayed request cannot act after logout or switching.
    /// Null means the requesting session is no longer valid; false means the
    /// requested device is missing. Never accept the public reference as a SID.
    /// </summary>
    public static Task<bool?> RevokeByReferenceForCurrentSessionAsync(
        EduOSDbContext db, long userId, string? currentSid, long? tenantId,
        string? reference, CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync<bool?>(async ct =>
        {
            if (!await IsValidAsync(db, userId, currentSid, tenantId))
                return null;
            if (!ValidReference(reference)) return false;

            var ids = await db.UserTokens.AsNoTracking()
                .Where(x => x.UserId == userId && x.LoginProvider == Provider)
                .Select(x => x.Name).Take(500).ToArrayAsync(ct);
            var targetSid = ids.FirstOrDefault(x => ValidSid(x) &&
                string.Equals(ToPublicReference(x), reference, StringComparison.Ordinal));
            if (targetSid == null) return false;

            var target = await db.UserTokens.SingleOrDefaultAsync(x =>
                x.UserId == userId && x.LoginProvider == Provider && x.Name == targetSid, ct);
            if (target == null) return false;
            db.UserTokens.Remove(target);
            await db.SaveChangesAsync(ct);
            return true;
        }, IsolationLevel.Serializable, cancellationToken);

    public static async Task<bool> RevokeByReferenceAsync(EduOSDbContext db, long userId, string? reference)
    {
        if (userId <= 0 || !ValidReference(reference)) return false;
        // Indexed by user/provider; cap legacy session rows rather than scanning all tenants.
        var ids = await db.UserTokens.AsNoTracking()
            .Where(x => x.UserId == userId && x.LoginProvider == Provider)
            .Select(x => x.Name).Take(500).ToArrayAsync();
        var sid = ids.FirstOrDefault(x => ValidSid(x) &&
            string.Equals(ToPublicReference(x), reference, StringComparison.Ordinal));
        return sid != null && await RevokeAsync(db, userId, sid);
    }

    public static async Task<bool> RevokeAsync(EduOSDbContext db, long userId, string sid)
    {
        if (!ValidSid(sid)) return false;
        var token = await db.UserTokens.SingleOrDefaultAsync(x =>
            x.UserId == userId && x.LoginProvider == Provider && x.Name == sid);
        if (token == null) return false;
        db.UserTokens.Remove(token);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Revoke other devices without invalidating the current tenant-bound session.</summary>
    public static Task<int?> RevokeOthersAsync(EduOSDbContext db, long userId,
        string? currentSid, long? tenantId, CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync<int?>(async ct =>
        {
            // Revalidate under the same serializable transaction as the delete.
            // A revoked or switched cookie must not sign out another device.
            if (!await IsValidAsync(db, userId, currentSid, tenantId)) return null;
            var others = await db.UserTokens.Where(x => x.UserId == userId &&
                x.LoginProvider == Provider && x.Name != currentSid).ToListAsync(ct);
            if (others.Count == 0) return 0;
            db.UserTokens.RemoveRange(others);
            await db.SaveChangesAsync(ct);
            return others.Count;
        }, IsolationLevel.Serializable, cancellationToken);

    public static async Task RevokeAllAsync(EduOSDbContext db, long userId)
    {
        var tokens = await db.UserTokens.Where(x =>
            x.UserId == userId && x.LoginProvider == Provider).ToListAsync();
        if (tokens.Count == 0) return;
        db.UserTokens.RemoveRange(tokens);
        await db.SaveChangesAsync();
    }

    private static SessionRecord? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SessionRecord>(json); }
        catch (JsonException) { return null; }
    }

    private static bool ValidReference(string? reference) => reference is { Length: 64 } &&
        reference.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool ValidSid(string? sid) => sid is { Length: 48 } &&
        sid.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string Truncate(string? value, int length)
    {
        value ??= "";
        return value.Length <= length ? value : value[..length];
    }
}
