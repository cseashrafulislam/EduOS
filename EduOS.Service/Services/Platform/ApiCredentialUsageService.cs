using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Platform;

/// <summary>
/// Narrow read-only integration endpoint. A key is not an Identity user or administrator.
/// Only the integration:read scope can read persisted usage snapshots.
/// </summary>
public sealed class ApiCredentialUsageService(
    EduOSDbContext db, TimeProvider clock, ILogger<ApiCredentialUsageService> logger) : IApiCredentialUsageService
{
    public const string RequiredScope = "integration:read";
    public static bool TryParseAuthorization(string? header, out string key)
    {
        key = string.Empty;
        if (header == null || !header.StartsWith("ApiKey ", StringComparison.Ordinal)) return false;
        var candidate = header["ApiKey ".Length..];
        if (candidate.Length != 49 || !candidate.StartsWith("eduos_", StringComparison.Ordinal) ||
            candidate.AsSpan(6).ToString().Any(c =>
                !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            return false;
        key = candidate;
        return true;
    }
    public static bool MatchesHash(string plaintext, string? hash)
    {
        if (hash is null || hash.Length != 64) return false;
        try
        {
            var expected = Convert.FromHexString(hash);
            return expected.Length == 32 && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)), expected);
        }
        catch (FormatException) { return false; }
    }
    public static bool HasScope(string? allowedScopes) =>
        (allowedScopes ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(RequiredScope, StringComparer.Ordinal);

    private static UsageStatisticsDto Map(EduOS.Core.Entities.SaaS.UsageStatistics x) => new()
    {
        PeriodStart = x.PeriodStart, PeriodEnd = x.PeriodEnd,
        ActiveStudents = x.ActiveStudents, ActiveEmployees = x.ActiveEmployees,
        ActiveCampuses = x.ActiveCampuses, StorageBytes = x.StorageBytes,
        SmsCount = x.SmsCount, EmailCount = x.EmailCount
    };

    public async Task<ApiResponse<PagedResult<UsageStatisticsDto>>> GetUsageAsync(
        string? authorizationHeader, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Invalid paging.", 400);
        if (!TryParseAuthorization(authorizationHeader, out var plaintext))
            return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Invalid API credential.", 401);
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            // Resolve tenant from the key itself; no caller-supplied tenant selector.
            var prefix = plaintext[..20];
            var candidates = await db.ApiCredentials.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.KeyPrefix == prefix && x.IsActive && !x.IsDeleted)
                .Take(20).ToListAsync(ct);
            var match = candidates.FirstOrDefault(x => MatchesHash(plaintext, x.KeyHash));
            if (match == null || match.TenantId <= 0 ||
                (match.ExpiresAt.HasValue && match.ExpiresAt.Value <= now))
                return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Invalid API credential.", 401);
            if (!HasScope(match.AllowedScopes))
                return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Credential lacks integration:read scope.", 403);
            var activeTenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                x => x.Id == match.TenantId && !x.IsDeleted && x.State == TenantState.Active &&
                     x.OnboardingStage == OnboardingStage.Completed && x.OnboardingCompletedAt != null, ct);
            if (!activeTenant)
                return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Institution access is inactive.", 403);
            // Match the canonical module entitlement window: expired/unpaid subscriptions
            // must not keep an integration credential operational.
            var subscribed = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                x => x.TenantId == match.TenantId && !x.IsDeleted &&
                     x.StartsAt <= now && x.EndsAt > now &&
                     (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                      x.State == SubscriptionState.Grace), ct);
            if (!subscribed)
                return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("An active subscription is required.", 403);
            // This scope can only be established by a cookie-free request.
            using var tenantScope = db.BeginSystemTenantScope(match.TenantId);
            var current = await db.ApiCredentials.FirstOrDefaultAsync(x =>
                x.Id == match.Id && x.TenantId == match.TenantId && x.IsActive && !x.IsDeleted, ct);
            if (current == null || !MatchesHash(plaintext, current.KeyHash) ||
                (current.ExpiresAt.HasValue && current.ExpiresAt.Value <= now))
                return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Invalid API credential.", 401);
            if (!HasScope(current.AllowedScopes))
                return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Credential lacks integration:read scope.", 403);
            if (!current.LastUsedAt.HasValue || current.LastUsedAt.Value < now.AddMinutes(-5))
            {
                current.LastUsedAt = now;
                await db.SaveChangesAsync(ct);
            }
            var query = db.UsageStatistics.AsNoTracking()
                .Where(x => x.TenantId == match.TenantId && !x.IsDeleted);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.PeriodStart).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return ApiResponse<PagedResult<UsageStatisticsDto>>.SuccessResponse(new()
            {
                Items = rows.Select(Map).ToList(), TotalCount = total, Page = page, PageSize = pageSize
            });
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Credential changed; retry.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "API credential usage retrieval failed");
            return ApiResponse<PagedResult<UsageStatisticsDto>>.ErrorResponse("Usage data is temporarily unavailable.", 503);
        }
    }
}
