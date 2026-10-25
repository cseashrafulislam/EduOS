using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Documents;

/// <summary>Tenant-only private document uploads. Unscanned files are never registered or served.</summary>
public sealed class DocumentFileService(EduOSDbContext db, ICurrentUserService user,
    IFileUploadService storage, ILogger<DocumentFileService> logger,
    IDocumentVerificationService verification) : IDocumentFileService
{
    private bool CanManage => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0;
    // Storage-provider results are untrusted until verified against the caller's tenant.
    // Never scan or delete a key belonging to a different tenant or namespace.
    internal static bool IsTenantDocumentKey(string? key, long tenantId) =>
        tenantId > 0 && !string.IsNullOrWhiteSpace(key) &&
        key.StartsWith($"tenant-{tenantId}/documents/", StringComparison.Ordinal) &&
        !key.Contains('\\') && !key.Split('/').Any(x => x is "." or ".." or "");

    // Match the document administration UI's supported formats on the server.
    // Filename and ContentType are both caller-controlled; this is a format-policy
    // check, not a substitute for the mandatory private malware scan.
    private static bool SupportedDocumentFormat(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase),
            ".jpg" or ".jpeg" => contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase),
            ".png" => contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase),
            ".webp" => contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase),
            ".doc" => contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase),
            ".docx" => contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    // Cleanup failure must not replace the scanner's original error or a request
    // cancellation. Log it for orphan-file reconciliation and fail closed.
    private async Task CleanupRejectedScanAsync(string storageKey)
    {
        try
        {
            if (!await storage.DeletePrivateAsync(storageKey))
                logger.LogError("Malware-rejected document cleanup returned false for tenant {TenantId}, key {StorageKey}",
                    user.TenantId, storageKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Malware-rejected document cleanup failed for tenant {TenantId}, key {StorageKey}",
                user.TenantId, storageKey);
        }
    }

    // A failed registration must not retain a private orphan. Cleanup errors are
    // logged rather than masking the original database failure/cancellation.
    private async Task CleanupRegisteredUploadFailureAsync(FileAsset asset, string storageKey)
    {
        db.Entry(asset).State = EntityState.Detached;
        try
        {
            if (!await storage.DeletePrivateAsync(storageKey))
                logger.LogError("Private document cleanup returned false for tenant {TenantId}, asset {Reference}",
                    user.TenantId, asset.PublicId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Private document cleanup failed for tenant {TenantId}, asset {Reference}",
                user.TenantId, asset.PublicId);
        }
    }

    // A competing request may register the same idempotency key after our initial
    // lookup but before our insert. Compare all upload invariants after the unique
    // key conflict; identical retries must return the canonical committed asset.
    private static bool MatchesVerifiedUpload(FileAsset existing, long length, string sha,
        string fileName, string contentType, long tenantId) =>
        existing.IsVerifiedSafe && existing.MalwareScanStateCode == "Clean" &&
        existing.Visibility == FileVisibility.Private && existing.StorageProvider == "LocalPrivate" &&
        existing.SizeBytes == length && existing.Sha256 == sha &&
        IsTenantDocumentKey(existing.StorageKey, tenantId) &&
        existing.OriginalFileName == fileName && existing.ContentType == contentType;

    private static FileAssetDto Map(FileAsset asset) => new()
    {
        Id = asset.Id, Reference = asset.PublicId, StorageProvider = asset.StorageProvider,
        OriginalFileName = asset.OriginalFileName, ContentType = asset.ContentType,
        SizeBytes = asset.SizeBytes, Sha256 = asset.Sha256, Visibility = asset.Visibility,
        IsVerifiedSafe = asset.IsVerifiedSafe, CreatedAt = asset.CreatedAt
    };

    public Task<ApiResponse<FileAssetDto>> UploadAsync(Guid clientRequestId,
        PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!CanManage)
            return Task.FromResult(ApiResponse<FileAssetDto>.ErrorResponse("Tenant administrator required.", 403));
        return UploadCoreAsync(clientRequestId, upload, ct);
    }

    // Student leave administrators may stage a scanned supporting file only after the
    // leave service has checked the selected enrollment. No document is attached here.
    public Task<ApiResponse<FileAssetDto>> UploadForLeaveAsync(Guid clientRequestId,
        PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || user.TenantId <= 0 || user.UserId <= 0 ||
            !(user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("VicePrincipal")))
            return Task.FromResult(ApiResponse<FileAssetDto>.ErrorResponse("Leave administrator required.", 403));
        return UploadCoreAsync(clientRequestId, upload, ct);
    }

    // A portal user can only stage a scanned file for an active student they control.
    // Attachment ownership is checked again in the serializable student-document transaction.
    public async Task<ApiResponse<FileAssetDto>> UploadPortalAsync(Guid studentReference,
        Guid clientRequestId, PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        var self = user.IsInRole("Student");
        var guardianRole = user.IsInRole("Guardian") || user.IsInRole("Parent");
        if (!user.IsAuthenticated || user.TenantId <= 0 || user.UserId <= 0 || (!self && !guardianRole))
            return ApiResponse<FileAssetDto>.ErrorResponse("Portal account required.", 403);
        var linkedIds = from link in db.StudentGuardians.AsNoTracking()
                        join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
                        where link.TenantId == user.TenantId && guardian.TenantId == user.TenantId &&
                            guardian.UserId == user.UserId && guardian.IsActive &&
                            !guardian.IsDeleted && !link.IsDeleted
                        select link.StudentId;
        var allowed = await db.Students.AsNoTracking().AnyAsync(student =>
            student.TenantId == user.TenantId && student.PublicId == studentReference &&
            student.StatusCode == "Active" && !student.IsDeleted &&
            ((self && student.UserId == user.UserId) ||
             (guardianRole && linkedIds.Contains(student.Id))), ct);
        if (!allowed) return ApiResponse<FileAssetDto>.ErrorResponse("Student not found.", 404);
        return await UploadCoreAsync(clientRequestId, upload, ct);
    }

    private async Task<ApiResponse<FileAssetDto>> UploadCoreAsync(Guid clientRequestId,
        PrivateFileUploadDto upload, CancellationToken ct)
    {
        if (clientRequestId == Guid.Empty || upload == null || upload.Content == null ||
            upload.Length is < 1 or > 5242880 || string.IsNullOrWhiteSpace(upload.FileName) ||
            upload.FileName.Length > 255 || string.IsNullOrWhiteSpace(upload.ContentType) ||
            upload.ContentType.Length > 150)
            return ApiResponse<FileAssetDto>.ErrorResponse("Invalid file or upload request.");
        var fileName = Path.GetFileName(upload.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName)) return ApiResponse<FileAssetDto>.ErrorResponse("Invalid filename.");
        if (!SupportedDocumentFormat(fileName, upload.ContentType))
            return ApiResponse<FileAssetDto>.ErrorResponse("Unsupported document format or content type.", 400);

        // Hash exactly the uploaded bytes before checking idempotency. A reused request ID
        // with different bytes or metadata is a conflict, never an alias to another file.
        // Do not trust the declared multipart length: bound actual bytes read before
        // allocating more memory or writing any private file to disk.
        await using var buffer = new MemoryStream();
        try
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await upload.Content.ReadAsync(chunk.AsMemory(0, chunk.Length), ct)) > 0)
            {
                if (buffer.Length + read > 5242880)
                    return ApiResponse<FileAssetDto>.ErrorResponse("File exceeds the 5 MB limit.", 413);
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            if (buffer.Length != upload.Length)
                return ApiResponse<FileAssetDto>.ErrorResponse("File length mismatch.", 400);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return ApiResponse<FileAssetDto>.ErrorResponse("Unable to read uploaded file.", 400); }
        var sha = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        var existing = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == clientRequestId && !x.IsDeleted, ct);
        if (existing != null)
        {
            return MatchesVerifiedUpload(existing, buffer.Length, sha, fileName, upload.ContentType, user.TenantId)
                ? ApiResponse<FileAssetDto>.SuccessResponse(Map(existing))
                : ApiResponse<FileAssetDto>.ErrorResponse("Upload request ID already used.", 409);
        }

        buffer.Position = 0;
        var file = new FormFile(buffer, 0, buffer.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(), ContentType = upload.ContentType
        };
        var stored = await storage.UploadPrivateAsync(file, "documents");
        if (!stored.Success || string.IsNullOrWhiteSpace(stored.FileUrl))
            return ApiResponse<FileAssetDto>.ErrorResponse(stored.ErrorMessage ?? "Private upload failed.", 400);
        if (!IsTenantDocumentKey(stored.FileUrl, user.TenantId))
        {
            // Do not delete the unexpected key: it might belong to another tenant.
            logger.LogError("Private document storage returned a key outside tenant {TenantId} document namespace.", user.TenantId);
            return ApiResponse<FileAssetDto>.ErrorResponse("Private document storage namespace mismatch.", 503);
        }

        // Windows Defender must return a clean scan and the same SHA-256. On Linux,
        // scanner outage or any failure, delete the quarantined file and fail closed.
        string? scannedSha;
        try { scannedSha = await storage.ScanPrivateForMalwareAsync(stored.FileUrl, ct); }
        catch (OperationCanceledException)
        {
            await CleanupRejectedScanAsync(stored.FileUrl);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Private document malware scan failed for tenant {TenantId}", user.TenantId);
            await CleanupRejectedScanAsync(stored.FileUrl);
            return ApiResponse<FileAssetDto>.ErrorResponse("File security scan unavailable or failed.", 503);
        }
        if (!string.Equals(scannedSha, sha, StringComparison.Ordinal))
        {
            await CleanupRejectedScanAsync(stored.FileUrl);
            return ApiResponse<FileAssetDto>.ErrorResponse("File security scan unavailable or failed.", 503);
        }
        var asset = new FileAsset
        {
            TenantId = user.TenantId, PublicId = clientRequestId, StorageProvider = "LocalPrivate",
            StorageKey = stored.FileUrl, OriginalFileName = fileName, ContentType = upload.ContentType,
            SizeBytes = buffer.Length, Sha256 = sha, UploadedByUserId = user.UserId,
            MalwareScanStateCode = "Clean", MalwareScanCompletedAt = DateTime.UtcNow,
            Visibility = FileVisibility.Private, IsVerifiedSafe = true, CreatedBy = user.UserId
        };
        try
        {
            db.FileAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return ApiResponse<FileAssetDto>.SuccessResponse(Map(asset));
        }
        catch (DbUpdateException ex)
        {
            // EF retains failed inserts as Added. Detach before cleanup, so a later
            // scoped SaveChanges cannot resurrect metadata for a deleted private file.
            logger.LogWarning(ex, "Document file registration conflict for tenant {TenantId}", user.TenantId);
            await CleanupRegisteredUploadFailureAsync(asset, stored.FileUrl);
            // Unique PublicId is enforced by SQL Server. Another request can win
            // the insert race after our initial lookup; return its verified asset
            // only if its bytes, metadata and tenant storage ownership all match.
            var winner = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == clientRequestId && !x.IsDeleted, ct);
            if (winner != null && MatchesVerifiedUpload(winner, buffer.Length, sha,
                fileName, upload.ContentType, user.TenantId))
                return ApiResponse<FileAssetDto>.SuccessResponse(Map(winner));
            return ApiResponse<FileAssetDto>.ErrorResponse("Upload registration conflict; retry with a new request ID.", 409);
        }
        catch (OperationCanceledException)
        {
            await CleanupRegisteredUploadFailureAsync(asset, stored.FileUrl);
            throw;
        }
        catch (Exception ex)
        {
            // Database outages/interceptor errors can throw other exceptions. They
            // must not leave a private orphan or an Added FileAsset in this scope.
            logger.LogError(ex, "Unexpected document registration failure for tenant {TenantId}", user.TenantId);
            await CleanupRegisteredUploadFailureAsync(asset, stored.FileUrl);
            return ApiResponse<FileAssetDto>.ErrorResponse("File registration unavailable. Please retry.", 503);
        }
    }

    // Recover verified uploads after a browser refresh without exposing other tenants' files.
    // Pending files are not downloadable until attached to an active permitted document.
    public async Task<ApiResponse<PagedResult<FileAssetDto>>> GetUnattachedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<PagedResult<FileAssetDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<FileAssetDto>>.ErrorResponse("Invalid pagination.", 400);
        var query = db.FileAssets.AsNoTracking().Where(f =>
            f.TenantId == user.TenantId && !f.IsDeleted && f.IsVerifiedSafe &&
            f.MalwareScanStateCode == "Clean" && f.Visibility == FileVisibility.Private &&
            f.StorageProvider == "LocalPrivate" && f.StorageKey.StartsWith("tenant-" + user.TenantId + "/documents/") &&
            !f.StorageKey.Contains("..") && !f.StorageKey.Contains("\\") &&
            !f.StorageKey.Contains("//") && !f.StorageKey.Contains("/./") &&
            !f.StorageKey.EndsWith("/.") && !f.StorageKey.EndsWith("/") &&
            !db.Documents.Any(d => d.TenantId == user.TenantId && !d.IsDeleted && d.FileAssetId == f.Id));
        var count = await query.CountAsync(ct);
        var files = await query.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<FileAssetDto>>.SuccessResponse(new PagedResult<FileAssetDto>
        { Items = files.Select(Map).ToList(), TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid fileReference,
        CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Tenant administrator required.", 403);
        if (fileReference == Guid.Empty) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Invalid file reference.");
        var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == fileReference && !x.IsDeleted &&
            x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
            x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", ct);
        if (asset == null) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("File not found.", 404);
        // Defense in depth: never resolve a storage key outside the authenticated tenant's
        // document namespace, even if an asset row is accidentally associated incorrectly.
        // Reject malformed persisted keys rather than normalizing backslashes into
        // apparently valid paths. The same invariant applies to upload and attachment.
        if (!IsTenantDocumentKey(asset.StorageKey, user.TenantId))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("File not found.", 404);
        // Fail closed for legacy or concurrent duplicate attachments. A private asset
        // must have exactly one document owner; a second permissive document must not
        // make a sensitive or verification-required file downloadable.
        var attachmentCount = await db.Documents.AsNoTracking().CountAsync(d =>
            d.TenantId == user.TenantId && d.FileAssetId == asset.Id && !d.IsDeleted, ct);
        if (attachmentCount != 1)
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Active document not found.", 404);
        // Resolve the one permitted document before opening private storage.
        // Protected types must use the canonical verification service, which checks
        // the approval decision, reviewer separation, expiry, integrity and audit.
        // Never fall back to the ordinary path after a protected download is denied.
        var now = DateTime.UtcNow;
        var attached = await (from document in db.Documents.AsNoTracking()
            join type in db.DocumentTypeDefinitions.AsNoTracking()
                on new { document.TenantId, Code = document.DocumentTypeCode }
                equals new { type.TenantId, type.Code }
            where document.TenantId == user.TenantId && document.FileAssetId == asset.Id &&
                !document.IsDeleted && document.IsActive &&
                (document.EntityType == null || document.EntityType == "" ||
                    (document.EntityType == "Tenant" && document.EntityId == user.TenantId)) &&
                (!document.ExpiresAt.HasValue || document.ExpiresAt.Value > now) &&
                !type.IsDeleted && type.IsActive
            select new { Document = document, Type = type }).FirstOrDefaultAsync(ct);
        if (attached == null) return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Active document not found.", 404);
        if (attached.Type.RequiresVerification || attached.Type.IsSensitive)
            return await verification.DownloadApprovedAsync(attached.Document.PublicId, ct);
        EduOS.Service.Helpers.Storage.FileDownloadResult? file;
        try { file = await storage.GetPrivateFileAsync(asset.StorageKey); }
        catch (IOException ex)
        {
            logger.LogError(ex, "Private document read failed for tenant {TenantId}, file {FileId}", user.TenantId, asset.Id);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Private file temporarily unavailable.", 503);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Private document access failed for tenant {TenantId}, file {FileId}", user.TenantId, asset.Id);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Private file temporarily unavailable.", 503);
        }
        if (file == null || file.Content.LongLength != asset.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(file.Content)), asset.Sha256, StringComparison.Ordinal))
        {
            logger.LogWarning("Private document missing or modified for tenant {TenantId}, file {FileId}", user.TenantId, asset.Id);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("File unavailable or integrity check failed.", 409);
        }
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = file.Content, ContentType = asset.ContentType,
            FileName = Path.GetFileName(asset.OriginalFileName.Replace('\\', '/'))
        });
    }
}
