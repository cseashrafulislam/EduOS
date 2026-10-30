using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace EduOS.Service.Services.Library;

/// <summary>
/// Tenant-wide licensed library resources. Reuses the existing private, scanned FileAsset
/// and Document source of truth, rather than introducing a second file table or public URL.
/// A resource is visible only to an active member of a campus in this institution.
/// </summary>
public sealed class LibraryDigitalResourceService(
    EduOSDbContext db, ICurrentUserService user, IDocumentFileService documentFiles,
    IFileUploadService storage, TimeProvider clock, ILogger<LibraryDigitalResourceService> logger)
    : ILibraryDigitalResourceService
{
    private const string TypeCode = "LIBRARY_DIGITAL";
    private const string EntityType = "LibraryBook";
    private const long MaxBytes = 5 * 1024 * 1024;
    private long Tenant => user.TenantId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool Authenticated => user.IsAuthenticated && Tenant > 0 && user.UserId > 0;
    // The library-wide licence is controlled by the institution administrator.
    // Campus-scoped staff cannot publish or revoke access for other campuses.
    private bool CanPublish => Authenticated && user.IsTenantAdmin && db.CurrentTenantId == Tenant;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);

    private IQueryable<long> ActiveCampusIds() => db.Campuses.AsNoTracking()
        .Where(x => x.TenantId == Tenant && x.IsActive && !x.IsDeleted).Select(x => x.Id);

    private IQueryable<long>? MemberCampusIds()
    {
        var campuses = ActiveCampusIds();
        var today = DateOnly.FromDateTime(Now);
        if (user.IsTenantAdmin) return campuses;
        if (user.IsInRole("Principal"))
            return db.UserCampusAccesses.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId && x.IsActive && !x.IsDeleted &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        if (user.IsInRole("Student"))
        {
            var studentIds = db.Students.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.StatusCode == "Active" && !x.IsDeleted).Select(x => x.Id);
            return db.StudentEnrollments.AsNoTracking().Where(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.IsCurrent &&
                x.State == EnrollmentState.Active && studentIds.Contains(x.StudentId) &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        }
        if (user.IsInRole("Librarian") || user.IsInRole("Teacher") || user.IsInRole("Employee") ||
            user.IsInRole("Staff"))
        {
            var employeeIds = db.Employees.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id);
            return db.EmployeeCampusAssignments.AsNoTracking().Where(x =>
                x.TenantId == Tenant && !x.IsDeleted && x.IsCurrent &&
                employeeIds.Contains(x.EmployeeId) && x.EffectiveFrom <= today &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today) &&
                campuses.Contains(x.CampusId)).Select(x => x.CampusId);
        }
        return null;
    }

    private async Task<bool> CanReadAsync(CancellationToken ct)
    {
        if (!Authenticated || db.CurrentTenantId != Tenant) return false;
        var campuses = MemberCampusIds();
        return campuses != null && await campuses.AnyAsync(ct);
    }

    private async Task<bool> ValidCampusScopeAsync(long? campusId, CancellationToken ct) =>
        !campusId.HasValue || (campusId.Value > 0 && await db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == Tenant && x.Id == campusId.Value && x.IsActive && !x.IsDeleted, ct));

    public async Task<ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>> GetCampusOptionsAsync(CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<IReadOnlyList<LibraryCampusOptionDto>>("Tenant administrator required.", 403);
        var campuses = await db.Campuses.AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Take(200)
            .Select(x => new LibraryCampusOptionDto { Id = x.Id, Name = x.Name, Code = x.Code })
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<LibraryCampusOptionDto>>.SuccessResponse(campuses);
    }

    private static bool IsSqlRegistrationConflict(Exception error)
    {
        // Only normalize recognized SQL write-contention codes. Unrelated storage,
        // authentication and programming failures must remain visible to diagnostics.
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number is 1205 or 2601 or 2627 or 3960)
                return true;
        }
        return false;
    }

    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (current.Length == 0 || string.IsNullOrWhiteSpace(supplied)) return false;
        try
        {
            var expected = Convert.FromBase64String(supplied);
            return current.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(current, expected);
        }
        catch (FormatException) { return false; }
    }

    private static DateTime? Expiry(DateOnly? date) =>
        date?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

    private bool ValidExpiry(DateOnly? date) =>
        !date.HasValue || date.Value >= DateOnly.FromDateTime(Now);

    private static LibraryDigitalResourceDto Map(Document document, FileAsset file,
        Guid bookReference, string bookTitle) => new()
    {
        Reference = document.PublicId, BookReference = bookReference, BookTitle = bookTitle,
        CampusId = document.LibraryCampusId,
        Title = document.Title ?? string.Empty, FileName = file.OriginalFileName,
        ContentType = file.ContentType, SizeBytes = file.SizeBytes, IsActive = document.IsActive,
        ExpiresOn = document.ExpiresAt.HasValue ? DateOnly.FromDateTime(document.ExpiresAt.Value) : null,
        VersionNo = document.VersionNo, CreatedAt = document.CreatedAt, RowVersion = Convert.ToBase64String(document.RowVersion)
    };

    private IQueryable<LibraryDigitalRow> ResourceQuery()
    {
        // The projection is tenant-filtered on all three source tables before paging.
        var query = from document in db.Documents.AsNoTracking()
               join book in db.Books.AsNoTracking()
                   on new { document.TenantId, Id = document.EntityId }
                   equals new { book.TenantId, Id = (long?)book.Id }
               join file in db.FileAssets.AsNoTracking()
                   on new { document.TenantId, Id = document.FileAssetId }
                   equals new { file.TenantId, file.Id }
               join definition in db.DocumentTypeDefinitions.AsNoTracking()
                   on new { document.TenantId, Code = document.DocumentTypeCode }
                   equals new { definition.TenantId, definition.Code }
               where definition.IsActive && !definition.IsDeleted &&
                   !definition.RequiresVerification && !definition.IsSensitive &&
                   document.TenantId == Tenant && !document.IsDeleted &&
                   document.EntityType == EntityType && document.DocumentTypeCode == TypeCode &&
                   !book.IsDeleted && !file.IsDeleted && file.IsVerifiedSafe &&
                   file.MalwareScanStateCode == "Clean" &&
                   file.Visibility == FileVisibility.Private && file.StorageProvider == "LocalPrivate" &&
                   !db.Documents.AsNoTracking().Any(other =>
                       other.TenantId == Tenant && !other.IsDeleted &&
                       other.FileAssetId == file.Id && other.Id != document.Id)
               select new LibraryDigitalRow { Document = document, Book = book, File = file };
        if (CanPublish) return query;
        var campuses = MemberCampusIds();
        // A campus licence is never widened by revoked membership or inactive campus.
        return campuses == null ? query.Where(x => false) : query.Where(x =>
            !x.Document.LibraryCampusId.HasValue ||
            campuses.Contains(x.Document.LibraryCampusId.Value));
    }

    private sealed class LibraryDigitalRow
    {
        public Document Document { get; set; } = null!;
        public EduOS.Core.Entities.Library.Book Book { get; set; } = null!;
        public FileAsset File { get; set; } = null!;
    }

    public async Task<ApiResponse<PagedResult<LibraryDigitalResourceDto>>> ListAsync(
        Guid? bookReference, string? search, int page, int pageSize, bool includeInactive,
        CancellationToken ct = default)
    {
        if (!await CanReadAsync(ct)) return Fail<PagedResult<LibraryDigitalResourceDto>>("Library access denied.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue ||
            search?.Length > 100 || bookReference == Guid.Empty)
            return Fail<PagedResult<LibraryDigitalResourceDto>>("Invalid digital library filters.");
        var query = ResourceQuery();
        if (!CanPublish || !includeInactive)
            query = query.Where(x => x.Document.IsActive && x.Book.IsActive &&
                (!x.Document.ExpiresAt.HasValue || x.Document.ExpiresAt > Now));
        if (bookReference.HasValue)
            query = query.Where(x => x.Book.PublicId == bookReference.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Book.Title.Contains(term) ||
                (x.Document.Title != null && x.Document.Title.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Document.CreatedAt)
            .ThenByDescending(x => x.Document.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                DocumentReference = x.Document.PublicId, x.Document.Title, x.Document.IsActive,
                x.Document.ExpiresAt, x.Document.CreatedAt, x.Document.RowVersion, x.Document.VersionNo,
                x.Document.LibraryCampusId, BookReference = x.Book.PublicId,
                BookTitle = x.Book.Title, x.File.OriginalFileName, x.File.ContentType, x.File.SizeBytes
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryDigitalResourceDto>>.SuccessResponse(
            new PagedResult<LibraryDigitalResourceDto>
            {
                Items = rows.Select(x => new LibraryDigitalResourceDto
                {
                    Reference = x.DocumentReference, BookReference = x.BookReference,
                    CampusId = x.LibraryCampusId,
                    BookTitle = x.BookTitle, Title = x.Title ?? string.Empty,
                    FileName = x.OriginalFileName, ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes, IsActive = x.IsActive,
                    ExpiresOn = x.ExpiresAt.HasValue ? DateOnly.FromDateTime(x.ExpiresAt.Value) : null,
                    VersionNo = x.VersionNo, CreatedAt = x.CreatedAt,
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    // Historical audit events remain immutable. Only tenant admins may investigate actor IDs.
    // Date windows are bounded and private file keys, audit JSON and borrower names never leave SQL.
    public async Task<ApiResponse<PagedResult<LibraryDigitalAuditDto>>> GetAuditTrailAsync(
        DateOnly? from, DateOnly? to, string? action, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (!CanPublish || db.CurrentTenantId != Tenant)
            return Fail<PagedResult<LibraryDigitalAuditDto>>("Tenant administrator required.", 403);
        var today = DateOnly.FromDateTime(Now);
        var first = from ?? today.AddDays(-30);
        var last = to ?? today;
        var filter = (action ?? "all").Trim().ToLowerInvariant();
        if (page < 1 || pageSize is < 1 or > 50 || ((long)page - 1) * pageSize > int.MaxValue ||
            first > last || last > today || last.DayNumber - first.DayNumber > 31 ||
            filter is not ("all" or "publish" or "replace" or "update" or "revoke" or "download"))
            return Fail<PagedResult<LibraryDigitalAuditDto>>("Invalid audit filters.");
        var begin = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = last.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = from audit in db.AuditLogs.AsNoTracking()
                    join doc in db.Documents.AsNoTracking() on audit.EntityId equals (long?)doc.Id
                    where audit.TenantId == Tenant && audit.IsSuccess &&
                        audit.EntityName == "LibraryDigitalResource" &&
                        audit.OccurredAt >= begin && audit.OccurredAt < end &&
                        doc.TenantId == Tenant && !doc.IsDeleted &&
                        doc.EntityType == EntityType && doc.DocumentTypeCode == TypeCode &&
                        (audit.Action == "LibraryDigitalResource.Publish" ||
                         audit.Action == "LibraryDigitalResource.Replace" ||
                         audit.Action == "LibraryDigitalResource.Update" ||
                         audit.Action == "LibraryDigitalResource.Revoke" ||
                         audit.Action == "LibraryDigitalResource.Download")
                    select new { audit, doc };
        if (filter != "all")
        {
            var code = "LibraryDigitalResource." + char.ToUpperInvariant(filter[0]) + filter[1..];
            query = query.Where(x => x.audit.Action == code);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.audit.OccurredAt)
            .ThenByDescending(x => x.audit.Id)
            .Skip((int)(((long)page - 1) * pageSize)).Take(pageSize)
            .Select(x => new LibraryDigitalAuditDto
            {
                ResourceReference = x.doc.PublicId,
                CurrentTitle = x.doc.Title ?? string.Empty,
                Action = x.audit.Action,
                VersionNo = x.doc.VersionNo,
                OccurredAtUtc = x.audit.OccurredAt,
                ActorUserId = x.audit.UserId
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryDigitalAuditDto>>.SuccessResponse(
            new PagedResult<LibraryDigitalAuditDto>
            { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    private AuditLog Audit(string action, long id, Guid reference) => new()
    {
        TenantId = Tenant, UserId = user.UserId, Action = action,
        EntityName = "LibraryDigitalResource", EntityId = id,
        CorrelationId = reference.ToString("N"), NewValue = action,
        IsSuccess = true, OccurredAt = Now
    };

    public async Task<ApiResponse<LibraryDigitalResourceDto>> UploadAsync(
        LibraryDigitalUploadRequestDto request, PrivateFileUploadDto file, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (request == null || request.RequestId == Guid.Empty || request.BookReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            file == null || file.Length is < 1 or > MaxBytes || !ValidExpiry(request.ExpiresOn))
            return Fail<LibraryDigitalResourceDto>("Invalid book, title, request ID or file.");
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == request.BookReference &&
            x.IsActive && !x.IsDeleted, ct);
        if (book == null) return Fail<LibraryDigitalResourceDto>("Active book not found.", 404);
        if (!await ValidCampusScopeAsync(request.CampusId, ct))
            return Fail<LibraryDigitalResourceDto>("Active campus in this institution required.", 400);
        // The existing document uploader enforces the private tenant namespace, bounded
        // byte stream, quota, malware scan, SHA-256 and idempotent upload receipt.
        var uploaded = await documentFiles.UploadAsync(request.RequestId, file, ct);
        if (!uploaded.Success || uploaded.Data == null)
            return Fail<LibraryDigitalResourceDto>(uploaded.Message, uploaded.StatusCode);
        var title = request.Title.Trim();
        var expiry = Expiry(request.ExpiresOn);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await ValidCampusScopeAsync(request.CampusId, token))
                    return Fail<LibraryDigitalResourceDto>("Campus licence changed. Reload and retry.", 409);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == uploaded.Data.Id &&
                    x.PublicId == request.RequestId && x.UploadedByUserId == user.UserId &&
                    !x.IsDeleted && x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", token);
                if (asset == null || !DocumentFileService.IsTenantDocumentKey(asset.StorageKey, Tenant))
                    return Fail<LibraryDigitalResourceDto>("Verified private file unavailable.", 403);
                var existing = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == request.RequestId && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.FileAssetId != asset.Id || existing.EntityId != book.Id ||
                        existing.EntityType != EntityType || existing.DocumentTypeCode != TypeCode ||
                        existing.Title != title || existing.ExpiresAt != expiry ||
                        existing.LibraryCampusId != request.CampusId || !existing.IsActive)
                        return Fail<LibraryDigitalResourceDto>("Request ID already used or resource revoked.", 409);
                    return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                        Map(existing, asset, book.PublicId, book.Title));
                }
                if (await db.Documents.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.FileAssetId == asset.Id && !x.IsDeleted, token))
                    return Fail<LibraryDigitalResourceDto>("File already attached.", 409);
                var definition = await db.DocumentTypeDefinitions.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Code == TypeCode && !x.IsDeleted, token);
                if (definition == null)
                {
                    definition = new DocumentTypeDefinition
                    {
                        TenantId = Tenant, Code = TypeCode, Name = "Library digital resource",
                        IsActive = true, RequiresVerification = false, IsSensitive = false,
                        CreatedBy = user.UserId, CreatedAt = Now
                    };
                    db.DocumentTypeDefinitions.Add(definition);
                    await db.SaveChangesAsync(token);
                }
                if (!definition.IsActive || definition.RequiresVerification || definition.IsSensitive)
                    return Fail<LibraryDigitalResourceDto>("Library document type requires administrator review.", 409);
                var row = new Document
                {
                    TenantId = Tenant, PublicId = request.RequestId, FileAssetId = asset.Id,
                    DocumentTypeCode = TypeCode, EntityType = EntityType, EntityId = book.Id,
                    Title = title, ExpiresAt = expiry, IsActive = true,
                    LibraryCampusId = request.CampusId,
                    VerificationStatusCode = "NotRequired",
                    CreatedBy = user.UserId, CreatedAt = Now
                };
                db.Documents.Add(row);
                await db.SaveChangesAsync(token);
                db.AuditLogs.Add(Audit("LibraryDigitalResource.Publish", row.Id, row.PublicId));
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(row, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital registration conflict for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource registration; retry the same request ID.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            // SQL Server can surface a serializable publish collision as SqlException
            // 1205 (deadlock) or 3960 (snapshot update conflict), sometimes wrapped
            // by EF's execution strategy. Never turn a concurrent replay into HTTP 500.
            logger.LogWarning(ex, "Library digital publication contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource registration; retry the same request ID.", 409);
        }
    }


    /// <summary>
    /// Atomically supersede an existing publication while preserving its immutable file
    /// and document history. Scanning runs before the short SQL transaction; an upload
    /// losing a concurrent replacement stays private and unattached for reconciliation.
    /// </summary>
    public async Task<ApiResponse<LibraryDigitalResourceDto>> ReplaceAsync(
        Guid reference, LibraryDigitalReplaceRequestDto request,
        PrivateFileUploadDto file, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            request.RequestId == reference || string.IsNullOrWhiteSpace(request.RowVersion) ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            file == null || file.Length is < 1 or > MaxBytes || !ValidExpiry(request.ExpiresOn))
            return Fail<LibraryDigitalResourceDto>("Valid resource, version, title and replacement file required.");

        var old = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
            x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, ct);
        if (old == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
        var replay = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == request.RequestId && !x.IsDeleted, ct);
        if (replay == null && (!old.IsActive || !VersionMatches(old.RowVersion, request.RowVersion)))
            return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
        if (replay != null && replay.SupersedesDocumentId != old.Id)
            return Fail<LibraryDigitalResourceDto>("Request ID already used.", 409);
        if (old.VersionNo < 1 || old.VersionNo == int.MaxValue)
            return Fail<LibraryDigitalResourceDto>("Resource version requires reconciliation.", 409);
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == old.EntityId && !x.IsDeleted && x.IsActive, ct);
        if (book == null) return Fail<LibraryDigitalResourceDto>("Active book unavailable.", 409);

        var uploaded = await documentFiles.UploadAsync(request.RequestId, file, ct);
        if (!uploaded.Success || uploaded.Data == null)
            return Fail<LibraryDigitalResourceDto>(uploaded.Message, uploaded.StatusCode);
        var title = request.Title.Trim();
        var expiry = Expiry(request.ExpiresOn);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var previous = await db.Documents.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
                    x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, token);
                if (previous == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == uploaded.Data.Id &&
                    x.PublicId == request.RequestId && x.UploadedByUserId == user.UserId &&
                    !x.IsDeleted && x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", token);
                if (asset == null || !DocumentFileService.IsTenantDocumentKey(asset.StorageKey, Tenant))
                    return Fail<LibraryDigitalResourceDto>("Verified replacement file unavailable.", 403);
                var existing = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == request.RequestId && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.SupersedesDocumentId != previous.Id ||
                        existing.VersionNo != previous.VersionNo + 1 ||
                        existing.EntityId != previous.EntityId || existing.EntityType != EntityType ||
                        existing.DocumentTypeCode != TypeCode || existing.FileAssetId != asset.Id ||
                        existing.Title != title || existing.ExpiresAt != expiry ||
                         existing.LibraryCampusId != previous.LibraryCampusId || !existing.IsActive ||
                         previous.IsActive)
                        return Fail<LibraryDigitalResourceDto>("Replacement replay differs or was revoked.", 409);
                    return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                        Map(existing, asset, book.PublicId, book.Title));
                }
                if (!previous.IsActive || !VersionMatches(previous.RowVersion, request.RowVersion))
                    return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
                if (await db.Documents.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.FileAssetId == asset.Id && !x.IsDeleted, token))
                    return Fail<LibraryDigitalResourceDto>("Replacement file already attached.", 409);
                if (!await db.DocumentTypeDefinitions.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.Code == TypeCode && x.IsActive &&
                    !x.IsDeleted && !x.RequiresVerification && !x.IsSensitive, token))
                    return Fail<LibraryDigitalResourceDto>("Library publication policy unavailable.", 409);
                previous.IsActive = false;
                previous.UpdatedAt = Now;
                previous.UpdatedBy = user.UserId;
                var next = new Document
                {
                    TenantId = Tenant, PublicId = request.RequestId,
                    FileAssetId = asset.Id, DocumentTypeCode = TypeCode,
                    EntityType = EntityType, EntityId = previous.EntityId,
                    SupersedesDocumentId = previous.Id, VersionNo = previous.VersionNo + 1,
                    Title = title, ExpiresAt = expiry, IsActive = true,
                    LibraryCampusId = previous.LibraryCampusId,
                    VerificationStatusCode = "NotRequired",
                    CreatedBy = user.UserId, CreatedAt = Now
                };
                db.Documents.Add(next);
                await db.SaveChangesAsync(token);
                var audit = Audit("LibraryDigitalResource.Replace", next.Id, next.PublicId);
                audit.OldValue = JsonSerializer.Serialize(new { PreviousReference = reference, previous.VersionNo });
                audit.NewValue = JsonSerializer.Serialize(new { NewReference = next.PublicId, next.VersionNo });
                db.AuditLogs.Add(audit);
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(next, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryDigitalResourceDto>("Resource changed concurrently.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital replacement contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent replacement. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<LibraryDigitalResourceDto>> UpdateMetadataAsync(
        Guid reference, LibraryDigitalMetadataRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || !ValidExpiry(request.ExpiresOn))
            return Fail<LibraryDigitalResourceDto>("Valid title, future expiry and row version required.");
        if (!await ValidCampusScopeAsync(request.CampusId, ct))
            return Fail<LibraryDigitalResourceDto>("Active campus in this institution required.", 400);
        var title = request.Title.Trim();
        var expiry = Expiry(request.ExpiresOn);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await ValidCampusScopeAsync(request.CampusId, token))
                    return Fail<LibraryDigitalResourceDto>("Campus licence changed. Reload and retry.", 409);
                var document = await db.Documents.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
                    x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, token);
                if (document == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
                if (!VersionMatches(document.RowVersion, request.RowVersion))
                    return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
                if (!document.IsActive)
                    return Fail<LibraryDigitalResourceDto>("Revoked resources cannot be edited.", 409);
                // Old metadata clients must not silently widen a restricted licence.
                if (document.LibraryCampusId.HasValue && !request.CampusId.HasValue &&
                    !request.ConfirmTenantWideAccess)
                    return Fail<LibraryDigitalResourceDto>("Confirm institute-wide access explicitly.", 409);
                var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == document.EntityId && !x.IsDeleted, token);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == document.FileAssetId && !x.IsDeleted &&
                    x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", token);
                if (book == null || asset == null ||
                    !DocumentFileService.IsTenantDocumentKey(asset.StorageKey, Tenant))
                    return Fail<LibraryDigitalResourceDto>("Verified resource unavailable.", 409);
                if (document.Title == title && document.ExpiresAt == expiry &&
                    document.LibraryCampusId == request.CampusId)
                    return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                        Map(document, asset, book.PublicId, book.Title));
                var before = JsonSerializer.Serialize(new { document.Title, document.ExpiresAt, document.LibraryCampusId });
                document.Title = title;
                document.ExpiresAt = expiry;
                document.LibraryCampusId = request.CampusId;
                document.UpdatedAt = Now;
                document.UpdatedBy = user.UserId;
                var audit = Audit("LibraryDigitalResource.Update", document.Id, reference);
                audit.OldValue = before;
                audit.NewValue = JsonSerializer.Serialize(new { document.Title, document.ExpiresAt, document.LibraryCampusId });
                db.AuditLogs.Add(audit);
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(document, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryDigitalResourceDto>("Resource changed concurrently.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital write contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource change. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<LibraryDigitalResourceDto>> RevokeAsync(
        Guid reference, LibraryDigitalRevokeRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish) return Fail<LibraryDigitalResourceDto>("Tenant administrator required.", 403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion))
            return Fail<LibraryDigitalResourceDto>("Resource and row version required.");
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                var row = await db.Documents.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted &&
                    x.DocumentTypeCode == TypeCode && x.EntityType == EntityType, token);
                if (row == null) return Fail<LibraryDigitalResourceDto>("Resource not found.", 404);
                if (!VersionMatches(row.RowVersion, request.RowVersion))
                    return Fail<LibraryDigitalResourceDto>("Resource changed. Reload and retry.", 409);
                var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.EntityId && !x.IsDeleted, token);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.FileAssetId && !x.IsDeleted, token);
                if (book == null || asset == null)
                    return Fail<LibraryDigitalResourceDto>("Resource references unavailable.", 409);
                if (row.IsActive)
                {
                    row.IsActive = false; row.UpdatedAt = Now; row.UpdatedBy = user.UserId;
                    db.AuditLogs.Add(Audit("LibraryDigitalResource.Revoke", row.Id, row.PublicId));
                    await db.SaveChangesAsync(token);
                }
                return ApiResponse<LibraryDigitalResourceDto>.SuccessResponse(
                    Map(row, asset, book.PublicId, book.Title));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryDigitalResourceDto>("Resource changed concurrently.", 409);
        }
        catch (Exception ex) when (IsSqlRegistrationConflict(ex))
        {
            logger.LogWarning(ex, "Library digital write contention for tenant {TenantId}", Tenant);
            return Fail<LibraryDigitalResourceDto>("Concurrent resource change. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (reference == Guid.Empty || !await CanReadAsync(ct))
            return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
        var row = await ResourceQuery().Where(x => x.Document.PublicId == reference &&
            x.Document.IsActive && x.Book.IsActive &&
            (!x.Document.ExpiresAt.HasValue || x.Document.ExpiresAt > Now))
            .Select(x => new { DocumentId = x.Document.Id, x.File.StorageKey, x.File.SizeBytes,
                x.File.Sha256, x.File.ContentType, x.File.OriginalFileName })
            .FirstOrDefaultAsync(ct);
        if (row == null || row.SizeBytes is < 1 or > MaxBytes ||
            !DocumentFileService.IsTenantDocumentKey(row.StorageKey, Tenant))
            return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
        // No public storage URL is returned; every download rechecks live membership,
        // resource state, private namespace and cryptographic file integrity.
        FileDownloadResult? stored;
        try { stored = await storage.GetPrivateFileAsync(row.StorageKey); }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Library private file read unavailable for tenant {TenantId}", Tenant);
            return Fail<DocumentFileDownloadDto>("File temporarily unavailable.", 503);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Library private file access unavailable for tenant {TenantId}", Tenant);
            return Fail<DocumentFileDownloadDto>("File temporarily unavailable.", 503);
        }
        if (stored == null || stored.Content.LongLength != row.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)), row.Sha256,
                StringComparison.Ordinal))
            return Fail<DocumentFileDownloadDto>("File integrity verification failed.", 409);

        // Private storage is asynchronous. A student may withdraw, lose campus
        // access, or an administrator may revoke the resource while bytes are
        // being read. Recheck the authoritative V1 membership and the complete
        // tenant-bound document/book/file chain before returning those bytes.
        // A successful pre-read authorization is not a durable download grant.
        if (!await CanReadAsync(ct) || !await ResourceQuery().AnyAsync(x =>
                x.Document.Id == row.DocumentId && x.Document.PublicId == reference &&
                x.Document.IsActive && x.Book.IsActive &&
                (!x.Document.ExpiresAt.HasValue || x.Document.ExpiresAt > Now), ct))
            return Fail<DocumentFileDownloadDto>("Resource not found.", 404);

        db.AuditLogs.Add(Audit("LibraryDigitalResource.Download", row.DocumentId, reference));
        await db.SaveChangesAsync(ct);
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = stored.Content, ContentType = row.ContentType,
            FileName = Path.GetFileName(row.OriginalFileName.Replace('\\', '/'))
        });
    }
}
