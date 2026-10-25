using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Files;
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

/// <summary>Tenant-scoped, malware-scanned student profile photos. PhotoUrl is a protected
/// relative API route, not a public filesystem URL or untrusted remote URL.</summary>
public sealed class StudentPhotoService(
    EduOSDbContext db, ICurrentUserService user, IDocumentFileService fileService,
    IFileUploadService storage, ILogger<StudentPhotoService> logger) : IStudentPhotoService
{
    private const long MaxPhotoBytes = 2 * 1024 * 1024;
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.UserId > 0 && user.TenantId > 0;
    private static ApiResponse<T> Fail<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static string PhotoPath(Guid student, Guid file) =>
        "/api/students/" + student.ToString("D") + "/photo/" + file.ToString("D");

    public static bool HasValidImageSignature(byte[] bytes, string fileName, string contentType)
    {
        if (bytes == null || bytes.Length < 12 || string.IsNullOrWhiteSpace(fileName) ||
            string.IsNullOrWhiteSpace(contentType)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if ((ext == ".jpg" || ext == ".jpeg") && contentType == "image/jpeg")
            return bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff;
        if (ext == ".png" && contentType == "image/png")
            return bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e &&
                bytes[3] == 0x47 && bytes[4] == 0x0d && bytes[5] == 0x0a &&
                bytes[6] == 0x1a && bytes[7] == 0x0a;
        if (ext == ".webp" && contentType == "image/webp")
            return bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
                bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 &&
                bytes[10] == 0x42 && bytes[11] == 0x50;
        return false;
    }

    private static bool VersionMatches(byte[] current, string? supplied)
    {
        try
        {
            var expected = Convert.FromBase64String(supplied ?? string.Empty);
            return expected.Length > 0 && expected.Length == current.Length &&
                CryptographicOperations.FixedTimeEquals(current, expected);
        }
        catch (FormatException) { return false; }
    }

    public async Task<ApiResponse<StudentPhotoDto>> UploadAsync(Guid studentReference,
        Guid requestId, string? rowVersion, PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<StudentPhotoDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || requestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(rowVersion) || upload == null ||
            upload.Length is < 12 or > MaxPhotoBytes || upload.Content == null)
            return Fail<StudentPhotoDto>("Valid student, request, version and image up to 2 MB required.");

        var before = await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (before == null) return Fail<StudentPhotoDto>("Student not found.", 404);
        if (!VersionMatches(before.RowVersion, rowVersion))
            return Fail<StudentPhotoDto>("Student changed. Reload the profile.", 409);

        await using var buffer = new MemoryStream();
        try
        {
            await upload.Content.CopyToAsync(buffer, ct);
            if (buffer.Length != upload.Length || buffer.Length > MaxPhotoBytes)
                return Fail<StudentPhotoDto>("Invalid image length.");
        }
        catch (IOException) { return Fail<StudentPhotoDto>("Unable to read image."); }
        var bytes = buffer.ToArray();
        if (!HasValidImageSignature(bytes, upload.FileName, upload.ContentType))
            return Fail<StudentPhotoDto>("Only valid JPG, PNG and WebP images are accepted.");
        buffer.Position = 0;

        // Existing file service owns private storage, mandatory malware scan and request-id replay.
        var staged = await fileService.UploadAsync(requestId, new PrivateFileUploadDto
        {
            FileName = upload.FileName, ContentType = upload.ContentType,
            Length = buffer.Length, Content = buffer
        }, ct);
        if (!staged.Success || staged.Data == null)
            return Fail<StudentPhotoDto>(staged.Message, staged.StatusCode);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var student = await db.Students.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == studentReference && !x.IsDeleted, ct);
            if (student == null) return Fail<StudentPhotoDto>("Student no longer available.", 404);
            var file = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == staged.Data.Reference &&
                x.UploadedByUserId == user.UserId && !x.IsDeleted &&
                x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate" &&
                x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                x.SizeBytes == bytes.LongLength && x.ContentType == upload.ContentType &&
                x.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)), ct);
            if (file == null || !DocumentFileService.IsTenantDocumentKey(file.StorageKey, user.TenantId))
                return Fail<StudentPhotoDto>("Verified private image unavailable.", 404);

            var path = PhotoPath(studentReference, file.PublicId);
            if (student.PhotoUrl == path)
                return ApiResponse<StudentPhotoDto>.SuccessResponse(new StudentPhotoDto
                {
                    PhotoUrl = path, RowVersion = Convert.ToBase64String(student.RowVersion)
                }, "Photo already saved.");
            if (!VersionMatches(student.RowVersion, rowVersion))
                return Fail<StudentPhotoDto>("Student changed. Reload the profile.", 409);

            // A request-id replay must not attach one asset to another student or document.
            var suffix = "/photo/" + file.PublicId.ToString("D");
            if (await db.Students.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    x.Id != student.Id && !x.IsDeleted && x.PhotoUrl != null &&
                    x.PhotoUrl.EndsWith(suffix), ct) ||
                await db.Documents.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                    !x.IsDeleted && x.FileAssetId == file.Id, ct) ||
                await db.StudentLeaveApplications.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && !x.IsDeleted && x.AttachmentFileId == file.Id, ct))
                return Fail<StudentPhotoDto>("File is already used by another workflow.", 409);

            var previous = student.PhotoUrl;
            student.PhotoUrl = path;
            student.UpdatedAt = DateTime.UtcNow;
            student.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = user.TenantId, UserId = user.UserId, EntityName = "Student",
                EntityId = student.Id, Action = "Student.PhotoUpdated",
                OldValue = previous, NewValue = path, RequestId = requestId.ToString("D"),
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<StudentPhotoDto>.SuccessResponse(new StudentPhotoDto
            {
                PhotoUrl = path, RowVersion = Convert.ToBase64String(student.RowVersion)
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<StudentPhotoDto>("Student changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Concurrent student photo conflict for tenant {TenantId}", user.TenantId);
            return Fail<StudentPhotoDto>("Photo update conflict. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Student photo update failed for tenant {TenantId}", user.TenantId);
            return Fail<StudentPhotoDto>("Photo update unavailable.", 503);
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid studentReference,
        Guid assetReference, CancellationToken ct = default)
    {
        if (!Allowed) return Fail<DocumentFileDownloadDto>("Tenant administrator required.", 403);
        if (studentReference == Guid.Empty || assetReference == Guid.Empty)
            return Fail<DocumentFileDownloadDto>("Invalid photo reference.");
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (student == null || student.PhotoUrl != PhotoPath(studentReference, assetReference))
            return Fail<DocumentFileDownloadDto>("Student photo not found.", 404);
        var file = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == assetReference && !x.IsDeleted &&
            x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate" &&
            x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
            (x.ContentType == "image/jpeg" || x.ContentType == "image/png" ||
                x.ContentType == "image/webp"), ct);
        if (file == null || !DocumentFileService.IsTenantDocumentKey(file.StorageKey, user.TenantId))
            return Fail<DocumentFileDownloadDto>("Student photo not found.", 404);
        try
        {
            var stored = await storage.GetPrivateFileAsync(file.StorageKey);
            if (stored == null || stored.Content.LongLength != file.SizeBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)),
                    file.Sha256, StringComparison.Ordinal) ||
                !HasValidImageSignature(stored.Content, file.OriginalFileName, file.ContentType))
                return Fail<DocumentFileDownloadDto>("Photo integrity verification failed.", 409);
            return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
            {
                Content = stored.Content, ContentType = file.ContentType, FileName = "student-photo"
            });
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Student photo storage unavailable for tenant {TenantId}", user.TenantId);
            return Fail<DocumentFileDownloadDto>("Photo temporarily unavailable.", 503);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Student photo storage denied for tenant {TenantId}", user.TenantId);
            return Fail<DocumentFileDownloadDto>("Photo temporarily unavailable.", 503);
        }
    }
}
