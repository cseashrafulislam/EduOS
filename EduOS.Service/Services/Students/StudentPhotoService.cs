using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.System;
using EduOS.Core.Entities.Students;
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
    private bool PortalAllowed => user.IsAuthenticated && user.UserId > 0 && user.TenantId > 0 &&
        (user.IsInRole("Student") || user.IsInRole("Guardian") || user.IsInRole("Parent"));

    private IQueryable<Student> PortalStudents(bool tracking = false)
    {
        var self = user.IsInRole("Student");
        var guardian = user.IsInRole("Guardian") || user.IsInRole("Parent");
        var linked = from link in db.StudentGuardians.AsNoTracking()
                     join g in db.Guardians.AsNoTracking() on link.GuardianId equals g.Id
                     where link.TenantId == user.TenantId && g.TenantId == user.TenantId &&
                           g.UserId == user.UserId && g.IsActive && !g.IsDeleted && !link.IsDeleted
                     select link.StudentId;
        var query = db.Students.Where(x => x.TenantId == user.TenantId &&
            x.StatusCode == "Active" && !x.IsDeleted &&
            ((self && x.UserId == user.UserId) || (guardian && linked.Contains(x.Id))));
        return tracking ? query : query.AsNoTracking();
    }
    private static string PortalPhotoPath(Guid student, Guid file) =>
        "/api/portal/students/" + student.ToString("D") + "/photo/" + file.ToString("D");
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

    public async Task<ApiResponse<StudentPhotoDto>> GetPortalAsync(Guid studentReference,
        CancellationToken ct = default)
    {
        if (!PortalAllowed) return Fail<StudentPhotoDto>("Portal account required.", 403);
        if (studentReference == Guid.Empty) return Fail<StudentPhotoDto>("Student reference required.");
        var student = await PortalStudents().FirstOrDefaultAsync(x => x.PublicId == studentReference, ct);
        if (student == null) return Fail<StudentPhotoDto>("Student not found.", 404);
        var path = string.Empty;
        var prefix = "/api/students/" + studentReference.ToString("D") + "/photo/";
        if (student.PhotoUrl != null && student.PhotoUrl.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParse(student.PhotoUrl[prefix.Length..], out var file) &&
            student.PhotoUrl == PhotoPath(studentReference, file))
            path = PortalPhotoPath(studentReference, file);
        return ApiResponse<StudentPhotoDto>.SuccessResponse(new StudentPhotoDto
        {
            PhotoUrl = path,
            RowVersion = Convert.ToBase64String(student.RowVersion)
        });
    }

    public Task<ApiResponse<StudentPhotoDto>> UploadAsync(Guid studentReference,
        Guid requestId, string? rowVersion, PrivateFileUploadDto upload, CancellationToken ct = default) =>
        UploadCoreAsync(studentReference, requestId, rowVersion, upload, false, ct);

    public Task<ApiResponse<StudentPhotoDto>> UploadPortalAsync(Guid studentReference,
        Guid requestId, string? rowVersion, PrivateFileUploadDto upload, CancellationToken ct = default) =>
        UploadCoreAsync(studentReference, requestId, rowVersion, upload, true, ct);

    private async Task<ApiResponse<StudentPhotoDto>> UploadCoreAsync(Guid studentReference,
        Guid requestId, string? rowVersion, PrivateFileUploadDto upload, bool portal, CancellationToken ct)
    {
        if (portal ? !PortalAllowed : !Allowed)
            return Fail<StudentPhotoDto>("Photo upload not authorized.", 403);
        if (studentReference == Guid.Empty || requestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(rowVersion) || upload == null ||
            upload.Length is < 12 or > MaxPhotoBytes || upload.Content == null)
            return Fail<StudentPhotoDto>("Valid student, request, version and image up to 2 MB required.");

        var before = portal
            ? await PortalStudents().FirstOrDefaultAsync(x => x.PublicId == studentReference, ct)
            : await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == studentReference && !x.IsDeleted, ct);
        if (before == null) return Fail<StudentPhotoDto>("Student not found.", 404);
        // Do not reject a stale token yet: an identical request-id replay must first
        // resolve its verified asset, then compare it to the already-saved photo.
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
        var stagedUpload = new PrivateFileUploadDto
        {
            FileName = upload.FileName, ContentType = upload.ContentType,
            Length = buffer.Length, Content = buffer
        };
        var staged = portal
            ? await fileService.UploadPortalAsync(studentReference, requestId, stagedUpload, ct)
            : await fileService.UploadAsync(requestId, stagedUpload, ct);
        if (!staged.Success || staged.Data == null)
            return Fail<StudentPhotoDto>(staged.Message, staged.StatusCode);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var student = portal
                ? await PortalStudents(true).FirstOrDefaultAsync(x => x.PublicId == studentReference, ct)
                : await db.Students.FirstOrDefaultAsync(x =>
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
                    PhotoUrl = portal ? PortalPhotoPath(studentReference, file.PublicId) : path,
                    RowVersion = Convert.ToBase64String(student.RowVersion)
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
                EntityId = student.Id, Action = portal ? "Student.PhotoPortalUpdated" : "Student.PhotoUpdated",
                OldValue = previous, NewValue = path, RequestId = requestId.ToString("D"),
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<StudentPhotoDto>.SuccessResponse(new StudentPhotoDto
            {
                PhotoUrl = portal ? PortalPhotoPath(studentReference, file.PublicId) : path,
                RowVersion = Convert.ToBase64String(student.RowVersion)
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

    public Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid studentReference,
        Guid assetReference, CancellationToken ct = default) =>
        DownloadCoreAsync(studentReference, assetReference, false, ct);

    public Task<ApiResponse<DocumentFileDownloadDto>> DownloadPortalAsync(Guid studentReference,
        Guid assetReference, CancellationToken ct = default) =>
        DownloadCoreAsync(studentReference, assetReference, true, ct);

    private async Task<ApiResponse<DocumentFileDownloadDto>> DownloadCoreAsync(Guid studentReference,
        Guid assetReference, bool portal, CancellationToken ct)
    {
        if (portal ? !PortalAllowed : !Allowed)
            return Fail<DocumentFileDownloadDto>("Photo access not authorized.", 403);
        if (studentReference == Guid.Empty || assetReference == Guid.Empty)
            return Fail<DocumentFileDownloadDto>("Invalid photo reference.");
        var student = portal
            ? await PortalStudents().FirstOrDefaultAsync(x => x.PublicId == studentReference, ct)
            : await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
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
