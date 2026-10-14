using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Documents;

public sealed partial class DocumentVerificationService : IDocumentVerificationService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly IFileUploadService storage;
    private readonly ILogger<DocumentVerificationService> logger;

    public DocumentVerificationService(EduOSDbContext db, ICurrentUserService user,
        IFileUploadService storage, ILogger<DocumentVerificationService> logger)
    {
        this.db = db;
        this.user = user;
        this.storage = storage;
        this.logger = logger;
    }

    private bool CanReview => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0 && user.UserId > 0;
    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static DocumentVerificationDto Map(Document d, FileAsset f) => new()
    {
        Reference = d.PublicId, FileReference = f.PublicId, DocumentTypeCode = d.DocumentTypeCode,
        Title = d.Title, ExpiresAt = d.ExpiresAt, IsActive = d.IsActive,
        VerificationStatusCode = d.VerificationStatusCode == "NotRequired" ? "Pending" : d.VerificationStatusCode,
        CreatedBy = d.CreatedBy, VerifiedByUserId = d.VerifiedByUserId,
        VerifiedAt = d.VerifiedAt, VerificationNote = d.VerificationNote,
        RowVersion = Convert.ToBase64String(d.RowVersion)
    };
    private static bool IsCleared(FileAsset f, long tenantId) =>
        f.TenantId == tenantId && !f.IsDeleted && f.IsVerifiedSafe &&
        f.MalwareScanStateCode == "Clean" && f.Visibility == FileVisibility.Private &&
        f.StorageProvider == "LocalPrivate" && DocumentFileService.IsTenantDocumentKey(f.StorageKey, tenantId);
    private AuditLog Audit(Document d, string action) => new()
    {
        TenantId = user.TenantId, UserId = user.UserId, Action = action,
        EntityName = "Document", EntityId = d.Id, CorrelationId = d.PublicId.ToString("D"),
        NewValue = d.VerificationStatusCode, IsSuccess = true, OccurredAt = DateTime.UtcNow
    };

    public async Task<ApiResponse<PagedResult<DocumentVerificationDto>>> GetReviewQueueAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanReview) return ApiResponse<PagedResult<DocumentVerificationDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize ||
            status is not (null or "" or "Pending" or "Approved" or "Rejected"))
            return ApiResponse<PagedResult<DocumentVerificationDto>>.ErrorResponse("Invalid review filter.", 400);
        var query = from d in db.Documents.AsNoTracking()
                    join t in db.DocumentTypeDefinitions.AsNoTracking()
                        on new { d.TenantId, Code = d.DocumentTypeCode } equals new { t.TenantId, t.Code }
                    join f in db.FileAssets.AsNoTracking()
                        on new { d.TenantId, Id = d.FileAssetId } equals new { f.TenantId, f.Id }
                    where d.TenantId == user.TenantId && !d.IsDeleted && !t.IsDeleted &&
                        (t.RequiresVerification || t.IsSensitive) && !f.IsDeleted &&
                        (d.EntityType == null || d.EntityType == "" ||
                            (d.EntityType == "Tenant" && d.EntityId == user.TenantId))
                    select new { Document = d, File = f };
        if (status == "Pending")
            query = query.Where(x => x.Document.VerificationStatusCode == "Pending" ||
                x.Document.VerificationStatusCode == "NotRequired");
        else if (!string.IsNullOrEmpty(status))
            query = query.Where(x => x.Document.VerificationStatusCode == status);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Document.CreatedAt)
            .ThenByDescending(x => x.Document.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<DocumentVerificationDto>>.SuccessResponse(new PagedResult<DocumentVerificationDto>
        {
            Items = rows.Select(x => Map(x.Document, x.File)).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }
}