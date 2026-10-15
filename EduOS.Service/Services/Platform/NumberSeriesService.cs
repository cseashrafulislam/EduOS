using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EduOS.Core.Common;
using EduOS.Core.DTOs.System;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Platform;

/// <summary>Tenant numbering policy and exactly-once counter issuance, without client-writable counters.</summary>
public sealed class NumberSeriesService(
    EduOSDbContext db, ICurrentUserService user, IUnitOfWork unit,
    ILogger<NumberSeriesService> logger) : INumberSeriesService
{
    private bool CanConfigure => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private bool CanIssue => user.IsAuthenticated && user.TenantId > 0;
    private static string Version(byte[] rowVersion) => Convert.ToBase64String(rowVersion);
    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static NumberSeriesDto Map(NumberSeries x) => new()
    {
        Id = x.Id, Key = x.Key, Prefix = x.Prefix, NextValue = x.NextValue,
        Padding = x.Padding, Suffix = x.Suffix, IsActive = x.IsActive,
        RowVersion = Version(x.RowVersion)
    };
    private static bool Valid(SaveNumberSeriesRequestDto x, string key, string prefix, string suffix) =>
        Regex.IsMatch(key, @"^[A-Z][A-Z0-9_.-]{1,99}$") &&
        prefix.Length <= 50 && suffix.Length <= 50 && x.Padding is >= 1 and <= 30 &&
        prefix.Length + suffix.Length + x.Padding <= 128;

    public async Task<ApiResponse<PagedResult<NumberSeriesDto>>> GetSeriesAsync(
        int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanConfigure) return ApiResponse<PagedResult<NumberSeriesDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<NumberSeriesDto>>.ErrorResponse("Invalid page.");
        var query = db.NumberSeries.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) return ApiResponse<PagedResult<NumberSeriesDto>>.ErrorResponse("Search is too long.");
            query = query.Where(x => x.Key.Contains(term));
        }
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.Key).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<NumberSeriesDto>>.SuccessResponse(new()
        {
            Items = rows.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<NumberSeriesDto>> SaveSeriesAsync(
        long? seriesId, SaveNumberSeriesRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanConfigure) return ApiResponse<NumberSeriesDto>.ErrorResponse("Tenant administrator required.", 403);
        if (request == null) return ApiResponse<NumberSeriesDto>.ErrorResponse("Request required.");
        var key = (request.Key ?? "").Trim().ToUpperInvariant();
        var prefix = (request.Prefix ?? "").Trim();
        var suffix = (request.Suffix ?? "").Trim();
        if (!Valid(request, key, prefix, suffix))
            return ApiResponse<NumberSeriesDto>.ErrorResponse("Invalid key, prefix, suffix or padding.");
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                NumberSeries row;
                if (seriesId.HasValue)
                {
                    row = await db.NumberSeries.FirstOrDefaultAsync(x =>
                        x.Id == seriesId.Value && x.TenantId == user.TenantId && !x.IsDeleted, token)
                        ?? throw new KeyNotFoundException("Series not found.");
                    if (!VersionMatches(row.RowVersion, request.RowVersion))
                        return ApiResponse<NumberSeriesDto>.ErrorResponse("Series changed; reload.", 409);
                    if (row.Key != key)
                        return ApiResponse<NumberSeriesDto>.ErrorResponse("Series key cannot be changed.", 409);
                    if (row.LastIssuedAt.HasValue &&
                        (row.Prefix != prefix || row.Padding != request.Padding ||
                         (row.Suffix ?? "") != suffix))
                        return ApiResponse<NumberSeriesDto>.ErrorResponse(
                            "Number format is locked after first issue.", 409);
                    row.Prefix = prefix; row.Padding = request.Padding; row.Suffix = suffix;
                    row.IsActive = request.IsActive;
                    row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId;
                }
                else
                {
                    row = new NumberSeries
                    {
                        TenantId = user.TenantId, Key = key, Prefix = prefix,
                        Padding = request.Padding, Suffix = suffix, NextValue = 1,
                        IsActive = request.IsActive, CreatedBy = user.UserId
                    };
                    db.NumberSeries.Add(row);
                }
                await db.SaveChangesAsync(token);
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId,
                    Action = seriesId.HasValue ? "number-series.updated" : "number-series.created",
                    EntityName = "NumberSeries", EntityId = row.Id, IsSuccess = true,
                    OccurredAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<NumberSeriesDto>.SuccessResponse(Map(row), "Number series saved.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (KeyNotFoundException) { return ApiResponse<NumberSeriesDto>.ErrorResponse("Series not found.", 404); }
        catch (DbUpdateConcurrencyException) { return ApiResponse<NumberSeriesDto>.ErrorResponse("Series changed; reload.", 409); }
        catch (DbUpdateException) { return ApiResponse<NumberSeriesDto>.ErrorResponse("Series key conflicts with existing data.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Number series save failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<NumberSeriesDto>.ErrorResponse("Could not save number series.", 500);
        }
    }

    public async Task<ApiResponse<string>> IssueNumberAsync(
        string seriesKey, Guid clientRequestId, CancellationToken cancellationToken = default)
    {
        if (!CanIssue) return ApiResponse<string>.ErrorResponse("Authenticated tenant required.", 403);
        var key = (seriesKey ?? "").Trim().ToUpperInvariant();
        if (clientRequestId == Guid.Empty || !Regex.IsMatch(key, @"^[A-Z][A-Z0-9_.-]{1,99}$"))
            return ApiResponse<string>.ErrorResponse("Valid key and request ID required.");
        var scope = "number-series.issue." + key;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                var previous = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Scope == scope &&
                    x.RequestId == clientRequestId && !x.IsDeleted, token);
                if (previous != null)
                    return previous.RequestHash == fingerprint && !string.IsNullOrEmpty(previous.ResultHash)
                        ? ApiResponse<string>.SuccessResponse(previous.ResultHash, "Previously issued number.")
                        : ApiResponse<string>.ErrorResponse("Request ID conflict.", 409);
                var row = await db.NumberSeries.FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Key == key && !x.IsDeleted, token);
                if (row == null) return ApiResponse<string>.ErrorResponse("Series not found.", 404);
                if (!row.IsActive) return ApiResponse<string>.ErrorResponse("Series inactive.", 409);
                if (row.NextValue <= 0 || row.NextValue == long.MaxValue ||
                    row.Padding is < 1 or > 30)
                    return ApiResponse<string>.ErrorResponse("Series counter exhausted or invalid.", 409);
                var issued = row.Prefix + row.NextValue.ToString("D" + row.Padding, CultureInfo.InvariantCulture) +
                    (row.Suffix ?? "");
                if (issued.Length > 128) return ApiResponse<string>.ErrorResponse("Issued number exceeds storage limit.", 409);
                row.NextValue++;
                row.LastIssuedAt = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = user.UserId;
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    TenantId = user.TenantId, Scope = scope, RequestId = clientRequestId,
                    RequestHash = fingerprint, ResultHash = issued,
                    ExpiresAt = DateTime.UtcNow.AddYears(10), CreatedBy = user.UserId
                });
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId,
                    Action = "number-series.issued", EntityName = "NumberSeries",
                    EntityId = row.Id, IsSuccess = true, OccurredAt = DateTime.UtcNow,
                    RequestId = clientRequestId.ToString("N")
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<string>.SuccessResponse(issued, "Number issued.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return ApiResponse<string>.ErrorResponse("Concurrent issue conflict; retry with the same request ID.", 409); }
        catch (DbUpdateException) { return ApiResponse<string>.ErrorResponse("Issue conflict; retry with the same request ID.", 409); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Number issue failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<string>.ErrorResponse("Could not issue number.", 500);
        }
    }
}
