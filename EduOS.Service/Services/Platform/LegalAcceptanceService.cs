using System.Data;
using System.Text.RegularExpressions;
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

namespace EduOS.Service.Services.Platform;

/// <summary>Host-published immutable legal versions and server-bound user consent.</summary>
public sealed class LegalAcceptanceService(
    EduOSDbContext db, ICurrentUserService user, IUnitOfWork unit,
    TimeProvider clock, ILogger<LegalAcceptanceService> logger) : ILegalAcceptanceService
{
    private bool CanRead => user.IsAuthenticated && user.UserId > 0 && (user.TenantId > 0 || user.IsSuperAdmin);
    private static LegalDocumentDto Map(LegalDocument x) => new()
    {
        Id = x.Id, Code = x.Code, Title = x.Title, Version = x.Version,
        Content = x.Content, EffectiveAt = x.EffectiveAt
    };
    private static UserLegalAcceptanceDto Map(UserLegalAcceptance x, string version) => new()
    {
        Id = x.Id, LegalDocumentId = x.LegalDocumentId, Version = version, AcceptedAt = x.AcceptedAt
    };

    /// <summary>One effective published version per code; future versions never displace current terms.</summary>
    public static IReadOnlyList<LegalDocument> SelectCurrent(IEnumerable<LegalDocument> documents, DateTime utcNow) =>
        documents.Where(x => !x.IsDeleted && x.IsActive && x.EffectiveAt <= utcNow)
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(d => d.EffectiveAt).ThenByDescending(d => d.Id).First())
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<ApiResponse<IReadOnlyList<LegalDocumentDto>>> GetRequiredDocumentsAsync(CancellationToken ct = default)
    {
        if (!CanRead) return ApiResponse<IReadOnlyList<LegalDocumentDto>>.ErrorResponse("Authenticated institution access required.", 403);
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.LegalDocuments.AsNoTracking().Where(x => !x.IsDeleted && x.IsActive && x.EffectiveAt <= now).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LegalDocumentDto>>.SuccessResponse(SelectCurrent(rows, now).Select(Map).ToArray());
    }

    public async Task<ApiResponse<IReadOnlyList<UserLegalAcceptanceDto>>> GetMyAcceptancesAsync(CancellationToken ct = default)
    {
        if (!CanRead) return ApiResponse<IReadOnlyList<UserLegalAcceptanceDto>>.ErrorResponse("Authenticated institution access required.", 403);
        long? tenantId = user.TenantId > 0 ? user.TenantId : null;
        var rows = await (from a in db.UserLegalAcceptances.IgnoreQueryFilters().AsNoTracking()
                          join d in db.LegalDocuments.AsNoTracking() on a.LegalDocumentId equals d.Id
                          where !a.IsDeleted && !d.IsDeleted && a.UserId == user.UserId && a.TenantId == tenantId
                          orderby a.AcceptedAt descending
                          select new { Acceptance = a, d.Version }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<UserLegalAcceptanceDto>>.SuccessResponse(rows.Select(x => Map(x.Acceptance, x.Version)).ToArray());
    }

    public async Task<ApiResponse<UserLegalAcceptanceDto>> AcceptAsync(AcceptLegalDocumentRequestDto request, CancellationToken ct = default)
    {
        if (!CanRead) return ApiResponse<UserLegalAcceptanceDto>.ErrorResponse("Authenticated institution access required.", 403);
        if (request == null || !request.Accepted || request.ClientRequestId == Guid.Empty || request.LegalDocumentId <= 0)
            return ApiResponse<UserLegalAcceptanceDto>.ErrorResponse("Explicit acceptance, document and request ID are required.");
        long? tenantId = user.TenantId > 0 ? user.TenantId : null;
        var actorId = user.UserId;
        var requestId = request.ClientRequestId.ToString("D");
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var candidates = await db.LegalDocuments.AsNoTracking().Where(x =>
                    !x.IsDeleted && x.IsActive && x.EffectiveAt <= now).ToListAsync(token);
                var selected = SelectCurrent(candidates, now).FirstOrDefault(x => x.Id == request.LegalDocumentId);
                if (selected == null)
                    return ApiResponse<UserLegalAcceptanceDto>.ErrorResponse("Document version is no longer current. Reload before accepting.", 409);
                var prior = await db.UserLegalAcceptances.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
                    !x.IsDeleted && x.UserId == actorId && x.TenantId == tenantId && x.LegalDocumentId == selected.Id, token);
                if (prior != null)
                    return ApiResponse<UserLegalAcceptanceDto>.SuccessResponse(Map(prior, selected.Version), "Already accepted.");
                var acceptance = new UserLegalAcceptance
                {
                    UserId = actorId, TenantId = tenantId, LegalDocumentId = selected.Id,
                    AcceptedAt = now, IpAddress = Truncate(user.IpAddress, 100),
                    UserAgent = Truncate(user.UserAgent, 500), CreatedAt = now, CreatedBy = actorId
                };
                db.UserLegalAcceptances.Add(acceptance);
                await unit.SaveChangesAsync(token);
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = tenantId, UserId = actorId, UserName = Truncate(user.FullName ?? user.Email, 200),
                    Action = "LegalAccepted", EntityName = "LegalDocument", EntityId = selected.Id,
                    NewValue = selected.Code + ":" + selected.Version, RequestId = requestId,
                    IpAddress = Truncate(user.IpAddress, 100), Endpoint = "/api/legal-documents/accept",
                    OccurredAt = now, CreatedAt = now, CreatedBy = actorId
                });
                await unit.SaveChangesAsync(token);
                return ApiResponse<UserLegalAcceptanceDto>.SuccessResponse(Map(acceptance, selected.Version), "Acceptance recorded.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateException) { return ApiResponse<UserLegalAcceptanceDto>.ErrorResponse("Concurrent acceptance detected. Refresh to confirm.", 409); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Legal acceptance failed for document {DocumentId}", request.LegalDocumentId);
            return ApiResponse<UserLegalAcceptanceDto>.ErrorResponse("Acceptance could not be recorded.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<LegalDocumentDto>>> GetPublishedDocumentsAsync(CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsSuperAdmin)
            return ApiResponse<IReadOnlyList<LegalDocumentDto>>.ErrorResponse("Platform administrator required.", 403);
        var rows = await db.LegalDocuments.AsNoTracking().Where(x => !x.IsDeleted)
            .OrderBy(x => x.Code).ThenByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Id).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LegalDocumentDto>>.SuccessResponse(rows.Select(Map).ToArray());
    }

    public async Task<ApiResponse<LegalDocumentDto>> PublishAsync(PublishLegalDocumentRequestDto request, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsSuperAdmin)
            return ApiResponse<LegalDocumentDto>.ErrorResponse("Platform administrator required.", 403);
        if (request == null) return ApiResponse<LegalDocumentDto>.ErrorResponse("Document required.");
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        var version = (request.Version ?? "").Trim();
        var title = (request.Title ?? "").Trim();
        var content = (request.Content ?? "").Trim();
        if (request.ClientRequestId == Guid.Empty || !Regex.IsMatch(code, @"^[A-Z][A-Z0-9_.-]{1,49}$") ||
            version.Length is < 1 or > 30 || title.Length is < 3 or > 200 ||
            content.Length is < 20 or > 50000 || request.EffectiveAt.Kind != DateTimeKind.Utc)
            return ApiResponse<LegalDocumentDto>.ErrorResponse("Valid code, version, title, content, UTC effective date and request ID are required.");
        try
        {
            return await unit.ExecuteInTransactionAsync(async token =>
            {
                var prior = await db.LegalDocuments.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Code == code && x.Version == version, token);
                if (prior != null)
                    return prior.IsActive && !prior.IsDeleted && prior.Title == title && prior.Content == content &&
                           prior.EffectiveAt == request.EffectiveAt
                        ? ApiResponse<LegalDocumentDto>.SuccessResponse(Map(prior), "Already published.")
                        : ApiResponse<LegalDocumentDto>.ErrorResponse("This document code/version already exists and is immutable.", 409);
                var now = clock.GetUtcNow().UtcDateTime;
                var document = new LegalDocument
                {
                    Code = code, Version = version, Title = title, Content = content,
                    EffectiveAt = request.EffectiveAt, IsActive = true,
                    CreatedAt = now, CreatedBy = user.UserId
                };
                db.LegalDocuments.Add(document);
                await unit.SaveChangesAsync(token);
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = null, UserId = user.UserId,
                    UserName = Truncate(user.FullName ?? user.Email, 200),
                    Action = "LegalPublished", EntityName = "LegalDocument", EntityId = document.Id,
                    NewValue = code + ":" + version, RequestId = request.ClientRequestId.ToString("D"),
                    IpAddress = Truncate(user.IpAddress, 100), Endpoint = "/api/legal-documents/admin",
                    OccurredAt = now, CreatedAt = now, CreatedBy = user.UserId
                });
                await unit.SaveChangesAsync(token);
                return ApiResponse<LegalDocumentDto>.SuccessResponse(Map(document), "Version published.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateException) { return ApiResponse<LegalDocumentDto>.ErrorResponse("Document version already exists or conflicted with another publisher.", 409); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Legal document publication failed for {Code}", code);
            return ApiResponse<LegalDocumentDto>.ErrorResponse("Document could not be published.", 500);
        }
    }

    private static string? Truncate(string? value, int length) =>
        value == null ? null : value[..Math.Min(value.Length, length)];
}
