using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Platform;

/// <summary>
/// Tenant administrator configuration only. API credentials are configuration-only; webhook test dispatch is explicitly administrator initiated.
/// Any future dispatcher must revalidate DNS/IP destinations at connection time to prevent DNS rebinding.
/// </summary>
public sealed partial class IntegrationAdministrationService(
    EduOSDbContext db, ICurrentUserService user, IDataProtectionProvider protection,
    IUnitOfWork unit, ILogger<IntegrationAdministrationService> logger) : IIntegrationAdministrationService
{
    private const string CredentialScope = "integration.api-credential.create";
    private bool CanManage => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static bool ValidPage(int page, int size) => page > 0 && size is >= 1 and <= 100 && page <= int.MaxValue / size;
    private static WebhookEndpointDto Map(WebhookEndpoint row) => new()
    {
        Id = row.Id, Name = row.Name, Url = row.Url,
        HasSecret = !string.IsNullOrEmpty(row.ProtectedSecret),
        EventCodes = row.EventCodes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
    private static WebhookDeliveryDto Map(WebhookDelivery row) => new()
    {
        Id = row.Id, EventId = row.EventId, WebhookEndpointId = row.WebhookEndpointId,
        EventCode = row.EventCode, State = row.State, AttemptCount = row.AttemptCount,
        LastAttemptAt = row.LastAttemptAt, NextAttemptAt = row.NextAttemptAt,
        FailureReason = row.FailureReason
    };
    private static ApiCredentialDto Map(ApiCredential row) => new()
    {
        Id = row.Id, Name = row.Name, KeyPrefix = row.KeyPrefix,
        AllowedScopes = (row.AllowedScopes ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        ExpiresAt = row.ExpiresAt, LastUsedAt = row.LastUsedAt,
        IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };

    // Configuration validation is not an SSRF defense for future HTTP dispatch:
    // a dispatcher must also pin a public DNS resolution and reject redirects/private addresses.
    public static bool IsPublicWebhookUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.Query) || string.IsNullOrEmpty(uri.IdnHost))
            return false;
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(host, out _) || Uri.CheckHostName(host) != UriHostNameType.Dns ||
            !host.Contains('.') || host.Length > 253) return false;
        var prohibited = new[] { ".localhost", ".local", ".internal", ".lan", ".home", ".test",
            ".invalid", ".example", ".onion", ".arpa", ".svc" };
        return !prohibited.Any(suffix => host == suffix[1..] || host.EndsWith(suffix, StringComparison.Ordinal));
    }
    private static string[]? NormalizeEventCodes(IReadOnlyList<string>? codes)
    {
        if (codes == null || codes.Count is < 1 or > 25) return null;
        var normalized = codes.Select(x => x?.Trim().ToUpperInvariant() ?? "").Distinct(StringComparer.Ordinal).ToArray();
        return normalized.Length is < 1 or > 25 ||
            normalized.Any(x => x.Length is < 2 or > 100 || !Regex.IsMatch(x, @"^[A-Z][A-Z0-9_.-]+$"))
            || string.Join(',', normalized).Length > 1000 ? null : normalized;
    }
    private static string[]? NormalizeScopes(IReadOnlyList<string>? scopes)
    {
        if (scopes == null || scopes.Count is < 1 or > 16) return null;
        var normalized = scopes.Select(x => x?.Trim().ToLowerInvariant() ?? "").Distinct(StringComparer.Ordinal).ToArray();
        return normalized.Length is < 1 or > 16 ||
            normalized.Any(x => x.Length is < 3 or > 100 || !Regex.IsMatch(x, @"^[a-z][a-z0-9:._-]+$")) ||
            string.Join(',', normalized).Length > 1000 ? null : normalized;
    }

    public async Task<ApiResponse<PagedResult<WebhookEndpointDto>>> GetWebhookEndpointsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<WebhookEndpointDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (!ValidPage(page, pageSize)) return ApiResponse<PagedResult<WebhookEndpointDto>>.ErrorResponse("Invalid paging.");
        var query = db.WebhookEndpoints.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<WebhookEndpointDto>>.SuccessResponse(new()
        {
            Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<WebhookEndpointDto>> SaveWebhookEndpointAsync(
        long? endpointId, SaveWebhookEndpointRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<WebhookEndpointDto>.ErrorResponse("Tenant administrator required.", 403);
        var events = NormalizeEventCodes(request?.EventCodes);
        if (request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 150 || !IsPublicWebhookUrl(request.Url) ||
            events == null || request.Secret?.Length > 256 ||
            (request.ClearSecret && !string.IsNullOrEmpty(request.Secret)) ||
            (!string.IsNullOrEmpty(request.Secret) && request.Secret.Length < 16) ||
            (endpointId.HasValue && endpointId.Value <= 0))
            return ApiResponse<WebhookEndpointDto>.ErrorResponse("Invalid webhook configuration.");
        try
        {
            WebhookEndpoint row;
            if (endpointId.HasValue)
            {
                row = await db.WebhookEndpoints.FirstOrDefaultAsync(
                    x => x.TenantId == user.TenantId && x.Id == endpointId.Value && !x.IsDeleted, cancellationToken)
                    ?? null!;
                if (row == null) return ApiResponse<WebhookEndpointDto>.ErrorResponse("Webhook not found.", 404);
                if (!VersionMatches(row.RowVersion, request.RowVersion))
                    return ApiResponse<WebhookEndpointDto>.ErrorResponse("Webhook changed; reload before saving.", 409);
            }
            else
            {
                row = new WebhookEndpoint { TenantId = user.TenantId, CreatedBy = user.UserId };
                db.WebhookEndpoints.Add(row);
            }
            if (request.ClearSecret && request.IsActive)
                return ApiResponse<WebhookEndpointDto>.ErrorResponse("Deactivate the webhook before clearing its secret.");
            row.Name = request.Name.Trim();
            row.Url = request.Url.Trim();
            row.EventCodes = string.Join(',', events);
            row.IsActive = request.IsActive;
            if (request.ClearSecret) row.ProtectedSecret = string.Empty;
            else if (!string.IsNullOrEmpty(request.Secret))
                row.ProtectedSecret = protection.CreateProtector("EduOS.Integrations.WebhookSecrets.v1").Protect(request.Secret);
            if (row.ProtectedSecret.Length > 4000 ||
                (row.IsActive && string.IsNullOrWhiteSpace(row.ProtectedSecret)))
                return ApiResponse<WebhookEndpointDto>.ErrorResponse("Active webhooks require a secret (at least 16 characters).");
            if (endpointId.HasValue) { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId; }
            await db.SaveChangesAsync(cancellationToken);
            return ApiResponse<WebhookEndpointDto>.SuccessResponse(Map(row));
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<WebhookEndpointDto>.ErrorResponse("Webhook changed; reload before saving.", 409); }
        catch (DbUpdateException)
        { return ApiResponse<WebhookEndpointDto>.ErrorResponse("Webhook could not be saved due to a data conflict.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Webhook configuration save failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<WebhookEndpointDto>.ErrorResponse("Webhook save failed.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<WebhookDeliveryDto>>> GetWebhookDeliveriesAsync(
        long endpointId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<WebhookDeliveryDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (endpointId <= 0 || !ValidPage(page, pageSize))
            return ApiResponse<PagedResult<WebhookDeliveryDto>>.ErrorResponse("Invalid delivery filter.");
        if (!await db.WebhookEndpoints.AsNoTracking().AnyAsync(
            x => x.TenantId == user.TenantId && x.Id == endpointId && !x.IsDeleted, cancellationToken))
            return ApiResponse<PagedResult<WebhookDeliveryDto>>.ErrorResponse("Webhook not found.", 404);
        var query = db.WebhookDeliveries.AsNoTracking().Where(
            x => x.TenantId == user.TenantId && x.WebhookEndpointId == endpointId && !x.IsDeleted);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<WebhookDeliveryDto>>.SuccessResponse(new()
        {
            Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<ApiCredentialDto>>> GetApiCredentialsAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<ApiCredentialDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (!ValidPage(page, pageSize)) return ApiResponse<PagedResult<ApiCredentialDto>>.ErrorResponse("Invalid paging.");
        var query = db.ApiCredentials.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<ApiCredentialDto>>.SuccessResponse(new()
        {
            Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<ApiCredentialCreatedDto>> CreateApiCredentialAsync(
        CreateApiCredentialRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<ApiCredentialCreatedDto>.ErrorResponse("Tenant administrator required.", 403);
        var scopes = NormalizeScopes(request?.AllowedScopes);
        var now = DateTime.UtcNow;
        if (request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
            scopes == null || (request.ExpiresAt.HasValue &&
                (request.ExpiresAt.Value.ToUniversalTime() <= now ||
                 request.ExpiresAt.Value.ToUniversalTime() > now.AddYears(5))))
            return ApiResponse<ApiCredentialCreatedDto>.ErrorResponse("Invalid API credential request.");
        var name = request.Name.Trim();
        var expires = request.ExpiresAt?.ToUniversalTime();
        var scopeText = string.Join(',', scopes);
        var canonical = name + "\n" + scopeText + "\n" + expires?.ToString("O");
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
            var used = await db.IdempotencyRecords.AsNoTracking().AnyAsync(
                x => x.TenantId == user.TenantId && x.Scope == CredentialScope &&
                     x.RequestId == request.ClientRequestId && !x.IsDeleted, token);
            if (used)
                return ApiResponse<ApiCredentialCreatedDto>.ErrorResponse(
                    "This request ID has already been processed. The original key cannot be shown again.", 409);
            var random = RandomNumberGenerator.GetBytes(32);
            var plaintext = "eduos_" + Convert.ToBase64String(random).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
            var row = new ApiCredential
            {
                TenantId = user.TenantId, Name = name, KeyPrefix = plaintext[..20],
                KeyHash = keyHash, AllowedScopes = scopeText, ExpiresAt = expires,
                IsActive = true, CreatedBy = user.UserId
            };
            db.ApiCredentials.Add(row);
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                TenantId = user.TenantId, Scope = CredentialScope,
                RequestId = request.ClientRequestId, RequestHash = requestHash,
                ResultHash = keyHash, ExpiresAt = now.AddDays(30), CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(cancellationToken);
            return ApiResponse<ApiCredentialCreatedDto>.SuccessResponse(new()
            {
                Credential = Map(row), PlainTextKey = plaintext
            }, "Credential created. Copy the key now; it will not be displayed again.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateException)
        { return ApiResponse<ApiCredentialCreatedDto>.ErrorResponse("Credential request already processed or conflicts with an existing key.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "API credential creation failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<ApiCredentialCreatedDto>.ErrorResponse("Credential creation failed.", 500);
        }
    }

    public async Task<ApiResponse<bool>> RevokeApiCredentialAsync(
        long credentialId, RevokeApiCredentialRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage) return ApiResponse<bool>.ErrorResponse("Tenant administrator required.", 403);
        if (credentialId <= 0 || request == null)
            return ApiResponse<bool>.ErrorResponse("Invalid credential request.");
        var row = await db.ApiCredentials.FirstOrDefaultAsync(
            x => x.Id == credentialId && x.TenantId == user.TenantId && !x.IsDeleted, cancellationToken);
        if (row == null) return ApiResponse<bool>.ErrorResponse("Credential not found.", 404);
        if (!row.IsActive) return ApiResponse<bool>.SuccessResponse(true, "Credential already revoked.");
        if (!VersionMatches(row.RowVersion, request.RowVersion))
            return ApiResponse<bool>.ErrorResponse("Credential changed; reload before revoking.", 409);
        row.IsActive = false; row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ApiResponse<bool>.SuccessResponse(true, "Credential revoked.");
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<bool>.ErrorResponse("Credential changed; reload before revoking.", 409); }
        catch (DbUpdateException)
        { return ApiResponse<bool>.ErrorResponse("Credential could not be revoked.", 409); }
    }
}
