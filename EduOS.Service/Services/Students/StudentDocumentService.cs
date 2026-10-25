using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Students;

/// <summary>
/// Student-owned metadata for already malware-scanned private uploads.
/// Sensitive or verification-required document types remain in the protected
/// document verification workflow, never the ordinary student download path.
/// </summary>
public sealed class StudentDocumentService(
    EduOSDbContext db, ICurrentUserService user, IFileUploadService storage,
    ILogger<StudentDocumentService> logger) : IStudentDocumentService
{
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0;
    private bool PortalAllowed => user.IsAuthenticated && user.TenantId > 0 && user.UserId > 0 &&
        (user.IsInRole("Student") || user.IsInRole("Guardian") || user.IsInRole("Parent"));

    // The portal never trusts a student reference without checking the signed-in user's
    // student identity or a live, tenant-owned guardian relationship.
    private Task<Student?> PortalStudentAsync(Guid reference, CancellationToken ct)
    {
        var self = user.IsInRole("Student");
        var guardianRole = user.IsInRole("Guardian") || user.IsInRole("Parent");
        var linkedIds = from link in db.StudentGuardians.AsNoTracking()
                        join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
                        where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
                            guardian.UserId == user.UserId && guardian.IsActive &&
                            !guardian.IsDeleted && !link.IsDeleted
                        select link.StudentId;
        return db.Students.AsNoTracking().FirstOrDefaultAsync(student =>
            student.TenantId == user.TenantId && student.PublicId == reference &&
            student.StatusCode == "Active" && !student.IsDeleted &&
            ((self && student.UserId == user.UserId) ||
             (guardianRole && linkedIds.Contains(student.Id))), ct);
    }
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private Task<Student?> StudentAsync(Guid reference, CancellationToken ct) =>
        db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
    private static bool MatchesVersion(byte[] current, string? supplied)
    {
        try
        {
            var version = Convert.FromBase64String(supplied ?? string.Empty);
            return version.Length > 0 && version.Length == current.Length &&
                CryptographicOperations.FixedTimeEquals(current, version);
        }
        catch (FormatException) { return false; }
    }
    private static StudentDocumentDto Map(Document document, FileAsset file) => new()
    {
        Reference = document.PublicId, FileReference = file.PublicId,
        DocumentTypeCode = document.DocumentTypeCode, Title = document.Title,
        FileName = file.OriginalFileName, ExpiresAt = document.ExpiresAt,
        IsActive = document.IsActive, RowVersion = Convert.ToBase64String(document.RowVersion)
    };
    private static PortalStudentDocumentDto MapPortal(Document document, FileAsset file) => new()
    {
        Reference = document.PublicId, DocumentTypeCode = document.DocumentTypeCode,
        Title = document.Title, FileName = Path.GetFileName(file.OriginalFileName.Replace('\\', '/')),
        ExpiresAt = document.ExpiresAt
    };

    // Read-only student/guardian projection: no document versions, storage keys or
    // sensitive/verification-required files are returned to the learner portal.
    public async Task<ApiResponse<IReadOnlyList<PortalStudentDocumentDto>>> GetPortalAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        if (!PortalAllowed) return Fail<IReadOnlyList<PortalStudentDocumentDto>>("Portal account required.", 403);
        if (studentReference == Guid.Empty) return Fail<IReadOnlyList<PortalStudentDocumentDto>>("Student reference required.");
        var student = await PortalStudentAsync(studentReference, ct);
        if (student == null) return Fail<IReadOnlyList<PortalStudentDocumentDto>>("Student not found.", 404);
        var now = DateTime.UtcNow;
        var rows = await (from document in db.Documents.AsNoTracking()
            join file in db.FileAssets.AsNoTracking() on document.FileAssetId equals file.Id
            join type in db.DocumentTypeDefinitions.AsNoTracking()
                on new { document.TenantId, Code = document.DocumentTypeCode }
                equals new { type.TenantId, type.Code }
            where document.TenantId == user.TenantId && file.TenantId == user.TenantId &&
                type.TenantId == user.TenantId && document.EntityType == "Student" &&
                document.EntityId == student.Id && !document.IsDeleted && document.IsActive &&
                !file.IsDeleted && !type.IsDeleted && type.IsActive &&
                !type.IsSensitive && !type.RequiresVerification &&
                (!document.ExpiresAt.HasValue || document.ExpiresAt > now) &&
                file.IsVerifiedSafe && file.MalwareScanStateCode == "Clean" &&
                file.Visibility == FileVisibility.Private && file.StorageProvider == "LocalPrivate" &&
                db.Documents.Count(other => other.TenantId == user.TenantId &&
                    other.FileAssetId == file.Id && !other.IsDeleted) == 1
            orderby document.CreatedAt descending, document.Id descending
            select new { Document = document, File = file }).Take(101).ToListAsync(ct);
        if (rows.Count > 100)
            return Fail<IReadOnlyList<PortalStudentDocumentDto>>("Document list exceeds portal limit.", 409);
        IReadOnlyList<PortalStudentDocumentDto> result = rows
            .Where(x => DocumentFileService.IsTenantDocumentKey(x.File.StorageKey, user.TenantId))
            .Select(x => MapPortal(x.Document, x.File)).ToList();
        return ApiResponse<IReadOnlyList<PortalStudentDocumentDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadPortalAsync(
        Guid studentReference, Guid documentReference, CancellationToken ct = default)
    {
        if (!PortalAllowed) return Fail<DocumentFileDownloadDto>("Portal account required.", 403);
        if (studentReference == Guid.Empty || documentReference == Guid.Empty)
            return Fail<DocumentFileDownloadDto>("Invalid student or document reference.");
        var student = await PortalStudentAsync(studentReference, ct);
        if (student == null) return Fail<DocumentFileDownloadDto>("Student not found.", 404);
        return await DownloadForStudentAsync(student, documentReference, ct);
    }

    private AuditLog Audit(string action, Document document) => new()
    {
        TenantId = user.TenantId, UserId = user.UserId, Action = action,
        EntityName = "Document", EntityId = document.Id == 0 ? null : document.Id,
        NewValue = document.PublicId.ToString("D"), OccurredAt = DateTime.UtcNow
    };

    public async Task<ApiResponse<IReadOnlyList<StudentDocumentTypeDto>>> GetTypesAsync(CancellationToken ct = default)
    {
        if (!Allowed) return Fail<IReadOnlyList<StudentDocumentTypeDto>>("Tenant administrator required.", 403);
        var types = await db.DocumentTypeDefinitions.AsNoTracking()
            .Where(x => x.TenantId == user.TenantId && !x.IsDeleted && x.IsActive &&
                !x.IsSensitive && !x.RequiresVerification)
            .OrderBy(x => x.Name).Select(x => new StudentDocumentTypeDto { Code = x.Code, Name = x.Name })
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<StudentDocumentTypeDto>>.SuccessResponse(types);
    }

    public async Task<ApiResponse<IReadOnlyList<StudentDocumentDto>>> GetAsync(Guid studentReference, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<IReadOnlyList<StudentDocumentDto>>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty) return Fail<IReadOnlyList<StudentDocumentDto>>("Student reference required.");
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Fail<IReadOnlyList<StudentDocumentDto>>("Student not found.", 404);
        var rows = await (from document in db.Documents.AsNoTracking()
            join file in db.FileAssets.AsNoTracking() on document.FileAssetId equals file.Id
            where document.TenantId == user.TenantId && file.TenantId == user.TenantId &&
                !document.IsDeleted && !file.IsDeleted &&
                document.EntityType == "Student" && document.EntityId == student.Id
            orderby document.CreatedAt descending, document.Id descending
            select new { Document = document, File = file }).Take(100).ToListAsync(ct);
        IReadOnlyList<StudentDocumentDto> result = rows.Select(x => Map(x.Document, x.File)).ToList();
        return ApiResponse<IReadOnlyList<StudentDocumentDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<StudentDocumentDto>> AttachAsync(Guid studentReference,
        AttachStudentDocumentDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentDocumentDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || request == null || request.FileReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.DocumentTypeCode) || request.DocumentTypeCode.Trim().Length > 100 ||
            request.Title?.Length > 250 || string.IsNullOrWhiteSpace(request.StudentRowVersion) ||
            request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow)
            return Fail<StudentDocumentDto>("Valid student, scanned file, document type and version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var student = await StudentAsync(studentReference, ct);
            if (student == null) return Fail<StudentDocumentDto>("Student not found.", 404);
            if (!MatchesVersion(student.RowVersion, request.StudentRowVersion))
                return Fail<StudentDocumentDto>("Student changed. Reload and retry.", 409);
            var code = request.DocumentTypeCode.Trim().ToUpperInvariant();
            var type = await db.DocumentTypeDefinitions.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Code == code && !x.IsDeleted && x.IsActive &&
                !x.IsSensitive && !x.RequiresVerification, ct);
            if (type == null) return Fail<StudentDocumentDto>(
                "This document type requires a separate protected verification workflow or is inactive.", 409);
            var file = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == request.FileReference && !x.IsDeleted &&
                x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", ct);
            if (file == null || !DocumentFileService.IsTenantDocumentKey(file.StorageKey, user.TenantId))
                return Fail<StudentDocumentDto>("Scanned private file not found.", 404);
            var prior = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.FileAssetId == file.Id && !x.IsDeleted, ct);
            var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
            if (prior != null)
            {
                if (prior.EntityType == "Student" && prior.EntityId == student.Id &&
                    prior.DocumentTypeCode == code && prior.Title == title &&
                    prior.ExpiresAt == request.ExpiresAt && prior.IsActive)
                    return ApiResponse<StudentDocumentDto>.SuccessResponse(Map(prior, file),
                        "Document already attached.");
                return Fail<StudentDocumentDto>("File is already attached to another document.", 409);
            }
            var now = DateTime.UtcNow;
            var document = new Document
            {
                TenantId = user.TenantId, PublicId = Guid.NewGuid(), FileAssetId = file.Id,
                EntityType = "Student", EntityId = student.Id, DocumentTypeCode = code,
                Title = title, ExpiresAt = request.ExpiresAt, IsActive = true,
                VerificationStatusCode = "NotRequired", CreatedAt = now, CreatedBy = user.UserId
            };
            db.Documents.Add(document);
            db.AuditLogs.Add(Audit("StudentDocument.Attached", document));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<StudentDocumentDto>.SuccessResponse(Map(document, file));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Student document attachment conflict in tenant {TenantId}", user.TenantId);
            return Fail<StudentDocumentDto>("File attachment conflicted with another request.", 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Student document attachment failed in tenant {TenantId}", user.TenantId);
            return Fail<StudentDocumentDto>("Unable to attach document.", 500);
        }
    }

    public async Task<ApiResponse<StudentDocumentDto>> DeactivateAsync(Guid studentReference,
        Guid documentReference, DeactivateStudentDocumentDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentDocumentDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || documentReference == Guid.Empty ||
            request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return Fail<StudentDocumentDto>("Document reference and version required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var student = await StudentAsync(studentReference, ct);
            if (student == null) return Fail<StudentDocumentDto>("Student not found.", 404);
            var row = await db.Documents.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                x.PublicId == documentReference && !x.IsDeleted &&
                x.EntityType == "Student" && x.EntityId == student.Id, ct);
            if (row == null) return Fail<StudentDocumentDto>("Student document not found.", 404);
            var file = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == row.FileAssetId && !x.IsDeleted, ct);
            if (file == null) return Fail<StudentDocumentDto>("File no longer exists.", 409);
            if (!row.IsActive)
                return ApiResponse<StudentDocumentDto>.SuccessResponse(Map(row, file),
                    "Document already inactive.");
            if (!MatchesVersion(row.RowVersion, request.RowVersion))
                return Fail<StudentDocumentDto>("Document changed. Reload and retry.", 409);
            row.IsActive = false;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = user.UserId;
            db.AuditLogs.Add(Audit("StudentDocument.Deactivated", row));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<StudentDocumentDto>.SuccessResponse(Map(row, file));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException) { return Fail<StudentDocumentDto>("Document changed. Reload and retry.", 409); }
        catch (DbUpdateException) { return Fail<StudentDocumentDto>("Document changed. Reload and retry.", 409); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Student document deactivation failed for tenant {TenantId}", user.TenantId);
            return Fail<StudentDocumentDto>("Unable to deactivate document.", 500);
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid studentReference,
        Guid documentReference, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<DocumentFileDownloadDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || documentReference == Guid.Empty)
            return Fail<DocumentFileDownloadDto>("Invalid student or document reference.");
        var student = await StudentAsync(studentReference, ct);
        if (student == null) return Fail<DocumentFileDownloadDto>("Student not found.", 404);
        return await DownloadForStudentAsync(student, documentReference, ct);
    }

    private async Task<ApiResponse<DocumentFileDownloadDto>> DownloadForStudentAsync(
        Student student, Guid documentReference, CancellationToken ct)
    {
        var row = await (from document in db.Documents.AsNoTracking()
            join file in db.FileAssets.AsNoTracking() on document.FileAssetId equals file.Id
            join type in db.DocumentTypeDefinitions.AsNoTracking()
                on new { document.TenantId, Code = document.DocumentTypeCode }
                equals new { type.TenantId, type.Code }
            where document.TenantId == user.TenantId && file.TenantId == user.TenantId &&
                type.TenantId == user.TenantId && document.PublicId == documentReference &&
                document.EntityType == "Student" && document.EntityId == student.Id &&
                !document.IsDeleted && document.IsActive && !file.IsDeleted &&
                !type.IsDeleted && type.IsActive && !type.IsSensitive && !type.RequiresVerification &&
                (!document.ExpiresAt.HasValue || document.ExpiresAt > DateTime.UtcNow) &&
                file.IsVerifiedSafe && file.MalwareScanStateCode == "Clean" &&
                file.Visibility == FileVisibility.Private && file.StorageProvider == "LocalPrivate"
            select new { Document = document, File = file }).FirstOrDefaultAsync(ct);
        if (row == null || !DocumentFileService.IsTenantDocumentKey(row.File.StorageKey, user.TenantId))
            return Fail<DocumentFileDownloadDto>("Active student document not found.", 404);
        if (await db.Documents.AsNoTracking().CountAsync(x =>
            x.TenantId == user.TenantId && x.FileAssetId == row.File.Id && !x.IsDeleted, ct) != 1)
            return Fail<DocumentFileDownloadDto>("Document ownership conflict.", 409);
        try
        {
            var stored = await storage.GetPrivateFileAsync(row.File.StorageKey);
            if (stored == null || stored.Content.LongLength != row.File.SizeBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)),
                    row.File.Sha256, StringComparison.Ordinal))
                return Fail<DocumentFileDownloadDto>("File integrity verification failed.", 409);
            return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
            {
                Content = stored.Content, ContentType = row.File.ContentType,
                FileName = Path.GetFileName(row.File.OriginalFileName.Replace('\\', '/'))
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Student document file unavailable for tenant {TenantId}", user.TenantId);
            return Fail<DocumentFileDownloadDto>("File temporarily unavailable.", 503);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Student document file access failed for tenant {TenantId}", user.TenantId);
            return Fail<DocumentFileDownloadDto>("File temporarily unavailable.", 503);
        }
    }
}
