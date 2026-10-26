using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentDocumentService
{
    public async Task<ApiResponse<IReadOnlyList<StudentDocumentTypeDto>>> GetPortalTypesAsync(
        CancellationToken ct = default)
    {
        if (!PortalAllowed) return Fail<IReadOnlyList<StudentDocumentTypeDto>>("Portal account required.", 403);
        var types = await db.DocumentTypeDefinitions.AsNoTracking()
            .Where(type => type.TenantId == user.TenantId && !type.IsDeleted && type.IsActive &&
                !type.IsSensitive && !type.RequiresVerification)
            .OrderBy(type => type.Name)
            .Select(type => new StudentDocumentTypeDto { Code = type.Code, Name = type.Name })
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<StudentDocumentTypeDto>>.SuccessResponse(types);
    }

    // One user action: stage a malware-scanned private asset, then atomically attach it.
    // A retry with the same request ID and bytes returns the same attachment.
    public async Task<ApiResponse<PortalStudentDocumentDto>> UploadPortalAsync(
        Guid studentReference, Guid requestId, string documentTypeCode, string? title,
        PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!PortalAllowed) return Fail<PortalStudentDocumentDto>("Portal account required.", 403);
        if (studentReference == Guid.Empty || requestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(documentTypeCode) || documentTypeCode.Trim().Length > 100 ||
            title?.Length > 250 || upload == null)
            return Fail<PortalStudentDocumentDto>("Valid student, request, document type and file required.");
        var student = await PortalStudentAsync(studentReference, ct);
        if (student == null) return Fail<PortalStudentDocumentDto>("Student not found.", 404);
        var expectedVersion = Convert.ToBase64String(student.RowVersion);
        var code = documentTypeCode.Trim().ToUpperInvariant();
        var cleanTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        var validType = await db.DocumentTypeDefinitions.AsNoTracking().AnyAsync(type =>
            type.TenantId == user.TenantId && type.Code == code && !type.IsDeleted &&
            type.IsActive && !type.IsSensitive && !type.RequiresVerification, ct);
        if (!validType) return Fail<PortalStudentDocumentDto>("Document type unavailable for portal upload.", 409);
        var uploaded = await fileService.UploadPortalAsync(studentReference, requestId, upload, ct);
        if (!uploaded.Success || uploaded.Data == null)
            return Fail<PortalStudentDocumentDto>(uploaded.Message, uploaded.StatusCode);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var current = await PortalStudentAsync(studentReference, ct);
            if (current == null) return Fail<PortalStudentDocumentDto>("Student access changed. Reload and retry.", 404);
            var file = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(asset =>
                asset.TenantId == user.TenantId && asset.PublicId == uploaded.Data.Reference &&
                asset.UploadedByUserId == user.UserId && !asset.IsDeleted &&
                asset.IsVerifiedSafe && asset.MalwareScanStateCode == "Clean" &&
                asset.Visibility == FileVisibility.Private && asset.StorageProvider == "LocalPrivate", ct);
            if (file == null || !DocumentFileService.IsTenantDocumentKey(file.StorageKey, user.TenantId))
                return Fail<PortalStudentDocumentDto>("Verified private upload not found.", 404);
            var existing = await db.Documents.AsNoTracking().FirstOrDefaultAsync(document =>
                document.TenantId == user.TenantId && document.FileAssetId == file.Id &&
                !document.IsDeleted, ct);
            if (existing != null)
            {
                if (existing.EntityType == "Student" && existing.EntityId == current.Id &&
                    existing.DocumentTypeCode == code && existing.Title == cleanTitle && existing.IsActive)
                    return ApiResponse<PortalStudentDocumentDto>.SuccessResponse(MapPortal(existing, file),
                        "Document already uploaded.");
                return Fail<PortalStudentDocumentDto>("File already attached to another document.", 409);
            }
            if (!MatchesVersion(current.RowVersion, expectedVersion))
                return Fail<PortalStudentDocumentDto>("Student changed. Reload and retry.", 409);
            var activeType = await db.DocumentTypeDefinitions.AsNoTracking().AnyAsync(type =>
                type.TenantId == user.TenantId && type.Code == code && !type.IsDeleted &&
                type.IsActive && !type.IsSensitive && !type.RequiresVerification, ct);
            if (!activeType) return Fail<PortalStudentDocumentDto>("Document type changed. Reload and retry.", 409);
            var document = new Document
            {
                TenantId = user.TenantId, PublicId = Guid.NewGuid(), FileAssetId = file.Id,
                EntityType = "Student", EntityId = current.Id, DocumentTypeCode = code,
                Title = cleanTitle, IsActive = true, VerificationStatusCode = "NotRequired",
                CreatedAt = DateTime.UtcNow, CreatedBy = user.UserId
            };
            db.Documents.Add(document);
            db.AuditLogs.Add(Audit("StudentDocument.PortalUploaded", document));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<PortalStudentDocumentDto>.SuccessResponse(MapPortal(document, file));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException) { return Fail<PortalStudentDocumentDto>("Document changed. Retry.", 409); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Portal document conflict in tenant {TenantId}", user.TenantId);
            return Fail<PortalStudentDocumentDto>("Concurrent document upload conflict. Retry.", 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Portal document upload failed in tenant {TenantId}", user.TenantId);
            return Fail<PortalStudentDocumentDto>("Unable to attach uploaded document.", 500);
        }
    }
}
