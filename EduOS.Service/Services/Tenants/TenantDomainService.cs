using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Tenants;

/// <summary>Only reads DNS TXT data from a fixed trusted HTTPS resolver; never requests tenant-controlled URLs.</summary>
public interface ITenantDomainTxtLookup
{
    Task<IReadOnlyList<string>> GetTxtAsync(string recordName, CancellationToken cancellationToken);
}

public sealed class TenantDomainTxtLookup(HttpClient http) : ITenantDomainTxtLookup
{
    public async Task<IReadOnlyList<string>> GetTxtAsync(string recordName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://cloudflare-dns.com/dns-query?name=" + Uri.EscapeDataString(recordName) + "&type=TXT");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (!root.TryGetProperty("Status", out var status) || status.GetInt32() != 0 ||
            !root.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var result = new List<string>();
        foreach (var answer in answers.EnumerateArray())
        {
            if (!answer.TryGetProperty("type", out var type) || type.GetInt32() != 16 ||
                !answer.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String) continue;
            var raw = data.GetString() ?? "";
            // DNS JSON quotes each TXT segment. Join adjacent segments without whitespace.
            var value = Regex.Replace(raw, "\"\\s+\"", "").Trim('"');
            if (value.Length <= 1024) result.Add(value);
        }
        return result;
    }
}

/// <summary>Tenant-owned custom domains. Host routing and TLS provisioning remain infrastructure responsibilities.</summary>
public sealed class TenantDomainService(
    EduOSDbContext db, IUnitOfWork unit, ICurrentUserService user,
    ITenantDomainTxtLookup dns, TimeProvider clock, ILogger<TenantDomainService> logger) : ITenantDomainService
{
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0;
    private static TenantDomainDto Map(TenantDomain d) => new()
    {
        Id = d.Id, HostName = d.HostName, VerificationStateCode = d.VerificationStateCode,
        IsPrimary = d.IsPrimary, IsActive = d.IsActive, VerifiedAt = d.VerifiedAt,
        RowVersion = Convert.ToBase64String(d.RowVersion)
    };
    public static string? NormalizeHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim().TrimEnd('.');
        if (value.Length > 253 || value.Contains(':') || value.Contains('/') || value.Contains('\\') ||
            value.Contains('*') || value.Contains('@') || value.Any(char.IsWhiteSpace)) return null;
        try { value = new IdnMapping().GetAscii(value).ToLowerInvariant(); }
        catch (ArgumentException) { return null; }
        var parts = value.Split('.');
        if (parts.Length < 2 || parts.Any(x => x.Length is < 1 or > 63 ||
            !Regex.IsMatch(x, "^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$"))) return null;
        var suffix = parts[^1];
        if (suffix.Length < 2 || suffix.All(char.IsDigit) ||
            new[] { "localhost", "local", "internal", "invalid", "test", "example", "onion", "arpa" }.Contains(suffix))
            return null;
        if (parts.Any(x => x == "localhost")) return null;
        return value;
    }
    private static bool VersionMatches(TenantDomain row, string? version)
    {
        try { return !string.IsNullOrWhiteSpace(version) &&
            row.RowVersion.AsSpan().SequenceEqual(Convert.FromBase64String(version)); }
        catch (FormatException) { return false; }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool MatchesHash(string stored, string candidate)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(stored), Convert.FromHexString(Hash(candidate))); }
        catch (FormatException) { return false; }
    }
    private static string RecordName(string host) => "_eduos-verify." + host;
    private void Audit(TenantDomain row, string action, string? oldValue, string? newValue, string requestId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user.TenantId, UserId = user.UserId, UserName = user.FullName,
            EntityName = "TenantDomain", EntityId = row.Id, Action = action,
            OldValue = oldValue, NewValue = newValue, RequestId = requestId,
            IpAddress = user.IpAddress, Endpoint = "/api/tenant-domains", OccurredAt = now,
            CreatedAt = now, CreatedBy = user.UserId
        });
    }
    public async Task<ApiResponse<IReadOnlyList<TenantDomainDto>>> GetDomainsAsync(CancellationToken cancellationToken = default)
    {
        if (!Allowed) return ApiResponse<IReadOnlyList<TenantDomainDto>>.ErrorResponse("Tenant administrator required.", 403);
        var rows = await db.TenantDomains.AsNoTracking().Where(x => x.TenantId == user.TenantId)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.HostName).Take(100).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<TenantDomainDto>>.SuccessResponse(rows.Select(Map).ToArray());
    }
    public async Task<ApiResponse<TenantDomainRegistrationDto>> RegisterDomainAsync(RegisterTenantDomainRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Allowed) return ApiResponse<TenantDomainRegistrationDto>.ErrorResponse("Tenant administrator required.", 403);
        var host = NormalizeHost(request?.HostName);
        if (host == null || request!.ClientRequestId == Guid.Empty)
            return ApiResponse<TenantDomainRegistrationDto>.ErrorResponse("Valid public DNS hostname and request ID are required.");
        try
        {
            return await unit.ExecuteInTransactionAsync(async ct =>
            {
                var prior = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.EntityName == "TenantDomain" &&
                    (x.Action == "DomainRegistered" || x.Action == "DomainChallengeRotated") &&
                    x.RequestId == request.ClientRequestId.ToString("D"), ct);
                var existing = await db.TenantDomains.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.HostName == host, ct);
                if (prior)
                    return existing != null && !existing.IsDeleted && existing.TenantId == user.TenantId
                        ? ApiResponse<TenantDomainRegistrationDto>.SuccessResponse(new()
                        {
                            Domain = Map(existing), VerificationRecordName = RecordName(host)
                        }, "Already registered. The DNS challenge is only displayed once; submit a new registration request to rotate it.")
                        : ApiResponse<TenantDomainRegistrationDto>.ErrorResponse("Request ID already used.", 409);
                if (existing != null && (existing.TenantId != user.TenantId || existing.IsDeleted || existing.VerifiedAt.HasValue))
                    return ApiResponse<TenantDomainRegistrationDto>.ErrorResponse("Hostname unavailable.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                var challenge = "eduos-domain-verification=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                var row = existing ?? new TenantDomain
                {
                    TenantId = user.TenantId, HostName = host, CreatedAt = now, CreatedBy = user.UserId
                };
                row.VerificationTokenHash = Hash(challenge);
                row.VerificationStateCode = "Pending"; row.VerifiedAt = null; row.IsActive = false; row.IsPrimary = false;
                row.UpdatedAt = now; row.UpdatedBy = user.UserId;
                if (existing == null) db.TenantDomains.Add(row);
                await unit.SaveChangesAsync(ct);
                Audit(row, existing == null ? "DomainRegistered" : "DomainChallengeRotated",
                    null, host, request.ClientRequestId.ToString("D"));
                await unit.SaveChangesAsync(ct);
                return ApiResponse<TenantDomainRegistrationDto>.SuccessResponse(new()
                {
                    Domain = Map(row), VerificationRecordName = RecordName(host),
                    VerificationRecordValue = challenge
                }, "Publish the TXT record, then verify DNS ownership.");
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DbUpdateException) { return ApiResponse<TenantDomainRegistrationDto>.ErrorResponse("Hostname or request conflicts with another operation.", 409); }
        catch (Exception ex) { logger.LogError(ex, "Custom domain registration failed."); return ApiResponse<TenantDomainRegistrationDto>.ErrorResponse("Domain registration failed.", 500); }
    }
    public async Task<ApiResponse<TenantDomainDto>> VerifyDomainAsync(long domainId, ChangeTenantDomainRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Allowed) return ApiResponse<TenantDomainDto>.ErrorResponse("Tenant administrator required.", 403);
        if (domainId <= 0 || string.IsNullOrWhiteSpace(request?.RowVersion))
            return ApiResponse<TenantDomainDto>.ErrorResponse("Domain ID and version are required.");
        var snapshot = await db.TenantDomains.AsNoTracking().FirstOrDefaultAsync(x => x.Id == domainId && x.TenantId == user.TenantId, cancellationToken);
        if (snapshot == null) return ApiResponse<TenantDomainDto>.ErrorResponse("Domain not found.", 404);
        if (!VersionMatches(snapshot, request!.RowVersion))
            return ApiResponse<TenantDomainDto>.ErrorResponse("Domain changed. Refresh and retry.", 409);
        bool matched;
        try
        {
            var records = await dns.GetTxtAsync(RecordName(snapshot.HostName), cancellationToken);
            matched = records.Any(x => x.StartsWith("eduos-domain-verification=", StringComparison.Ordinal) &&
                MatchesHash(snapshot.VerificationTokenHash, x));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "DNS TXT lookup unavailable for domain {Id}", domainId); return ApiResponse<TenantDomainDto>.ErrorResponse("DNS verification is temporarily unavailable.", 503); }
        if (!matched) return ApiResponse<TenantDomainDto>.ErrorResponse("Required DNS TXT ownership proof was not found.", 409);
        return await ChangeAsync(domainId, request.RowVersion, "DomainVerified", async (row, ct) =>
        {
            if (!string.Equals(row.VerificationTokenHash, snapshot.VerificationTokenHash, StringComparison.Ordinal))
                return "DNS challenge was rotated. Refresh and retry.";
            row.VerificationStateCode = "Verified"; row.VerifiedAt = clock.GetUtcNow().UtcDateTime;
            row.IsActive = true; return null;
        }, cancellationToken);
    }
    public Task<ApiResponse<TenantDomainDto>> SetPrimaryAsync(long domainId, ChangeTenantDomainRequestDto request, CancellationToken cancellationToken = default) =>
        ChangeAsync(domainId, request?.RowVersion, "DomainPrimarySet", async (row, ct) =>
        {
            if (!row.IsActive || row.VerifiedAt == null || row.VerificationStateCode != "Verified")
                return "Only verified active domains can become primary.";
            var current = await db.TenantDomains.Where(x => x.TenantId == user.TenantId &&
                x.IsPrimary && x.IsActive && x.Id != row.Id).ToListAsync(ct);
            foreach (var item in current) { item.IsPrimary = false; item.UpdatedAt = clock.GetUtcNow().UtcDateTime; item.UpdatedBy = user.UserId; }
            // Flush the old primary inside the transaction before satisfying the filtered unique index.
            if (current.Count != 0) await unit.SaveChangesAsync(ct);
            row.IsPrimary = true; return null;
        }, cancellationToken);
    public Task<ApiResponse<TenantDomainDto>> DeactivateAsync(long domainId, ChangeTenantDomainRequestDto request, CancellationToken cancellationToken = default) =>
        ChangeAsync(domainId, request?.RowVersion, "DomainDeactivated", (row, ct) =>
        {
            if (!row.IsActive) return Task.FromResult<string?>("Domain is already inactive.");
            row.IsActive = false; row.IsPrimary = false; return Task.FromResult<string?>(null);
        }, cancellationToken);
    private async Task<ApiResponse<TenantDomainDto>> ChangeAsync(long id, string? version, string action,
        Func<TenantDomain, CancellationToken, Task<string?>> mutate, CancellationToken cancellationToken)
    {
        if (!Allowed) return ApiResponse<TenantDomainDto>.ErrorResponse("Tenant administrator required.", 403);
        if (id <= 0 || string.IsNullOrWhiteSpace(version))
            return ApiResponse<TenantDomainDto>.ErrorResponse("Domain ID and version are required.");
        try
        {
            return await unit.ExecuteInTransactionAsync(async ct =>
            {
                var row = await db.TenantDomains.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == user.TenantId, ct);
                if (row == null) return ApiResponse<TenantDomainDto>.ErrorResponse("Domain not found.", 404);
                if (!VersionMatches(row, version))
                    return ApiResponse<TenantDomainDto>.ErrorResponse("Domain changed. Refresh and retry.", 409);
                var old = row.VerificationStateCode + "|" + row.IsActive + "|" + row.IsPrimary;
                var failure = await mutate(row, ct);
                if (failure != null) return ApiResponse<TenantDomainDto>.ErrorResponse(failure, 409);
                row.UpdatedAt = clock.GetUtcNow().UtcDateTime; row.UpdatedBy = user.UserId;
                await unit.SaveChangesAsync(ct);
                Audit(row, action, old, row.VerificationStateCode + "|" + row.IsActive + "|" + row.IsPrimary, Guid.NewGuid().ToString("D"));
                await unit.SaveChangesAsync(ct);
                return ApiResponse<TenantDomainDto>.SuccessResponse(Map(row));
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException) { return ApiResponse<TenantDomainDto>.ErrorResponse("Domain changed. Refresh and retry.", 409); }
        catch (DbUpdateException) { return ApiResponse<TenantDomainDto>.ErrorResponse("Concurrent primary-domain update. Refresh and retry.", 409); }
        catch (Exception ex) { logger.LogError(ex, "Domain operation {Action} failed.", action); return ApiResponse<TenantDomainDto>.ErrorResponse("Domain update failed.", 500); }
    }
}
