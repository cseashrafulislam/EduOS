using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Documents;

public sealed partial class DocumentVerificationService
{
    public Task<ApiResponse<DocumentFileDownloadDto>> DownloadForReviewAsync(
        Guid documentReference, CancellationToken ct = default) =>
        DownloadInternalAsync(documentReference, preview: true, ct);

    public Task<ApiResponse<DocumentFileDownloadDto>> DownloadApprovedAsync(
        Guid documentReference, CancellationToken ct = default) =>
        DownloadInternalAsync(documentReference, preview: false, ct);

    private async Task<ApiResponse<DocumentFileDownloadDto>> DownloadInternalAsync(
        Guid reference, bool preview, CancellationToken ct)
    {
        if (!CanReview) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Tenant administrator required.", 403);
        if (reference == Guid.Empty) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Invalid reference.", 400);
        var record = await (from documentRow in db.Documents.AsNoTracking()
                            join t in db.DocumentTypeDefinitions.AsNoTracking()
                                on new { documentRow.TenantId, Code = documentRow.DocumentTypeCode } equals new { t.TenantId, t.Code }
                            join assetRow in db.FileAssets.AsNoTracking()
                                on new { documentRow.TenantId, Id = documentRow.FileAssetId } equals new { assetRow.TenantId, assetRow.Id }
                            where documentRow.TenantId == user.TenantId && documentRow.PublicId == reference &&
                                !documentRow.IsDeleted && !t.IsDeleted && t.IsActive && (t.RequiresVerification || t.IsSensitive) &&
                                (documentRow.EntityType == null || documentRow.EntityType == "" ||
                                    (documentRow.EntityType == "Tenant" && documentRow.EntityId == user.TenantId))
                            select new { Document = documentRow, File = assetRow }).FirstOrDefaultAsync(ct);
        if (record == null) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Document not found.", 404);
        var d = record.Document;
        var f = record.File;
        if (!IsCleared(f, user.TenantId))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("File not security-cleared.", 403);
        if (await db.Documents.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
            x.FileAssetId == f.Id && !x.IsDeleted, ct) != 1)
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("File attachment conflict.", 409);
        if (preview)
        {
            if (!d.CreatedBy.HasValue || d.CreatedBy == user.UserId ||
                d.VerificationStatusCode is not ("Pending" or "NotRequired"))
                return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Review preview is unavailable.", 403);
        }
        else if (!d.IsActive || d.VerificationStatusCode != "Approved" || (d.UpdatedAt > d.VerifiedAt) ||
            !d.VerifiedByUserId.HasValue || !d.VerifiedAt.HasValue || d.VerifiedByUserId == d.CreatedBy ||
            (d.ExpiresAt.HasValue && d.ExpiresAt <= DateTime.UtcNow))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Approved document is unavailable.", 404);
        EduOS.Service.Helpers.Storage.FileDownloadResult? file;
        try { file = await storage.GetPrivateFileAsync(f.StorageKey); }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Document review file read failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Private file unavailable.", 503);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Document review file access failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Private file unavailable.", 503);
        }
        if (file == null || file.Content.LongLength != f.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(file.Content)), f.Sha256, StringComparison.Ordinal))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("File integrity verification failed.", 409);
        var audit = Audit(d, preview ? "Document.ReviewViewed" : "Document.ApprovedDownloaded");
        db.AuditLogs.Add(audit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            db.Entry(audit).State = EntityState.Detached;
            logger.LogError(ex, "Document read audit persistence failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Secure document audit unavailable.", 503);
        }
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = file.Content, ContentType = f.ContentType,
            FileName = Path.GetFileName(f.OriginalFileName.Replace('\\', '/'))
        });
    }
}