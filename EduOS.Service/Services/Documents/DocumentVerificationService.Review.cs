using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Documents;

public sealed partial class DocumentVerificationService
{
    public async Task<ApiResponse<DocumentVerificationDto>> ReviewAsync(
        Guid documentReference, DocumentVerificationDecisionDto request, CancellationToken ct = default)
    {
        if (!CanReview) return ApiResponse<DocumentVerificationDto>.ErrorResponse("Tenant administrator required.", 403);
        if (documentReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.Note?.Length > 500 ||
            (!request.Approve && string.IsNullOrWhiteSpace(request.Note)))
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Valid review version and rejection reason required.", 400);
        var d = await db.Documents.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            x.PublicId == documentReference && !x.IsDeleted, ct);
        if (d == null) return ApiResponse<DocumentVerificationDto>.ErrorResponse("Document not found.", 404);
        if ((!string.IsNullOrWhiteSpace(d.EntityType) && d.EntityType != "Tenant") ||
            (d.EntityType == "Tenant" && d.EntityId != user.TenantId))
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Owning module must review this document.", 403);
        if (!d.CreatedBy.HasValue || d.CreatedBy == user.UserId)
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Creator and reviewer must be distinct.", 403);
        if (!VersionMatches(d.RowVersion, request.RowVersion))
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Document changed. Reload and retry.", 409);
        if (d.VerificationStatusCode is "Approved" or "Rejected")
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Review is final; upload a new document version.", 409);
        if (d.VerificationStatusCode is not ("NotRequired" or "Pending"))
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Invalid review state.", 409);
        var t = await db.DocumentTypeDefinitions.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Code == d.DocumentTypeCode && !x.IsDeleted && x.IsActive, ct);
        if (t == null || (!t.RequiresVerification && !t.IsSensitive))
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Protected review not required.", 409);
        var f = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Id == d.FileAssetId, ct);
        if (f == null || !IsCleared(f, user.TenantId))
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Private file is not security-cleared.", 403);
        if (await db.Documents.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
            x.FileAssetId == f.Id && !x.IsDeleted, ct) != 1)
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("File attachment conflict.", 409);
        if (request.Approve && d.ExpiresAt.HasValue && d.ExpiresAt <= DateTime.UtcNow)
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Expired documents cannot be approved.", 409);
        var lastChanged = d.UpdatedAt ?? d.CreatedAt;
        if (!await db.AuditLogs.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
            x.UserId == user.UserId && x.EntityName == "Document" && x.EntityId == d.Id &&
            x.Action == "Document.ReviewViewed" && x.IsSuccess && x.OccurredAt >= lastChanged, ct))
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Inspect the protected file before deciding.", 409);
        var decisionTime = DateTime.UtcNow;
        d.VerificationStatusCode = request.Approve ? "Approved" : "Rejected";
        d.VerifiedByUserId = user.UserId;
        d.VerifiedAt = decisionTime;
        d.VerificationNote = request.Note?.Trim();
        d.IsActive = request.Approve;
        d.UpdatedAt = decisionTime;
        d.UpdatedBy = user.UserId;
        var audit = Audit(d, request.Approve ? "Document.ReviewApproved" : "Document.ReviewRejected");
        db.AuditLogs.Add(audit);
        try
        {
            await db.SaveChangesAsync(ct);
            return ApiResponse<DocumentVerificationDto>.SuccessResponse(Map(d, f));
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(d).State = EntityState.Detached;
            db.Entry(audit).State = EntityState.Detached;
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Document changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.Entry(d).State = EntityState.Detached;
            db.Entry(audit).State = EntityState.Detached;
            logger.LogWarning(ex, "Document review save conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentVerificationDto>.ErrorResponse("Document review conflict.", 409);
        }
    }
}