using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

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
                sessionId = x.Name, isCurrent = x.Name == currentSid,
                tenantId = x.Record!.TenantId, createdAtUtc = x.Record.CreatedAtUtc,
                expiresAtUtc = x.Record.ExpiresAtUtc, ipAddress = x.Record.IpAddress,
                userAgent = x.Record.UserAgent
            }).ToArray();
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

    private static bool ValidSid(string? sid) => sid is { Length: 48 } &&
        sid.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string Truncate(string? value, int length)
    {
        value ??= "";
        return value.Length <= length ? value : value[..length];
    }
}
