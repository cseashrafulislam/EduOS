using System.Data;
using System.Security.Cryptography;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.Research;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.SaaS;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Research;

/// <summary>Dedicated scanned private evidence. Links are immutable and version-bound.</summary>
public sealed class ResearchEvidenceFileService(EduOSDbContext db, ResearchWorkflowAccess access,
    IFileUploadService storage, TimeProvider clock, ILogger<ResearchEvidenceFileService> logger)
    : IResearchEvidenceFileService
{
    private const long MaxBytes = 5L * 1024 * 1024;
    private const int MaxFilesPerRevision = 5;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Research evidence unavailable.", 404);
    private static ApiResponse<T> Conflict<T>() => ApiResponse<T>.ErrorResponse("Evidence changed; reload and retry.", 409);
    private static bool Purpose(string value) => value is "proposal" or "accreditation";
    private static bool EvidenceWritable(string purpose, string state) =>
        state is "Draft" or "ChangesRequested" ||
        (purpose == "proposal" && state == "EthicsRejected");
    private static string Folder(string purpose) => purpose == "proposal" ? "research-evidence" : "accreditation-evidence";
    internal static bool TenantKey(string? key, long tenant, string purpose) =>
        Purpose(purpose) && tenant > 0 && !string.IsNullOrWhiteSpace(key) &&
        key.StartsWith($"tenant-{tenant}/{Folder(purpose)}/", StringComparison.Ordinal) &&
        !key.Contains('\\') && !key.Split('/').Any(x => x is "" or "." or "..");
    private static bool Supported(string name, string mime) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".pdf" => mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase),
            ".png" => mime.Equals("image/png", StringComparison.OrdinalIgnoreCase),
            ".jpg" or ".jpeg" => mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase),
            ".webp" => mime.Equals("image/webp", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private async Task<ParentRow?> ParentAsync(string purpose, Guid reference, CancellationToken ct)
    {
        if (!Purpose(purpose) || reference == Guid.Empty) return null;
        var sql = purpose == "proposal" ? """
            SELECT Id, CampusId, OwnerUserId, CurrentRevision, StateCode,
                   SupervisorUserId, ReviewerUserId
            FROM dbo.ResearchProposals WHERE TenantId = {0} AND PublicId = {1}
            """ : """
            SELECT Id, CampusId, OwnerUserId, CurrentRevision, StateCode,
                   CAST(NULL AS bigint) AS SupervisorUserId, ReviewerUserId
            FROM dbo.AccreditationAssessments WHERE TenantId = {0} AND PublicId = {1}
            """;
        var rows = await db.Database.SqlQueryRaw<ParentRow>(sql, access.TenantId, reference).ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    private static bool Owner(string purpose, ParentRow p, long actor, IReadOnlySet<string> roles) =>
        p.OwnerUserId == actor && (purpose == "proposal"
            ? ResearchWorkflowAccess.Has(roles, "TEACHER", "RESEARCHER", "RESEARCHCOORDINATOR", "TENANTADMIN", "PRINCIPAL")
            : ResearchWorkflowAccess.Has(roles, "TEACHER", "ACCREDITATIONOFFICER", "ACCREDITATIONCOORDINATOR", "TENANTADMIN", "PRINCIPAL"));
    private static bool Reviewer(string purpose, ParentRow p, long actor, IReadOnlySet<string> roles) =>
        p.OwnerUserId != actor && p.CurrentRevision > 0 && (purpose == "proposal"
            ? (p.SupervisorUserId == actor && ResearchWorkflowAccess.Has(roles, "TEACHER", "PRINCIPAL", "RESEARCHSUPERVISOR")) ||
              (p.ReviewerUserId == actor && ResearchWorkflowAccess.Has(roles, "TEACHER", "RESEARCHREVIEWER")) ||
              (p.StateCode == "ReviewRecommended" && ResearchWorkflowAccess.Has(roles, "ETHICSREVIEWER"))
            : p.ReviewerUserId == actor && ResearchWorkflowAccess.Has(roles, "ACCREDITATIONREVIEWER", "TEACHER", "PRINCIPAL"));
    private async Task<(ParentRow? Parent, bool Owner, bool Reviewer)> AuthorizedAsync(
        string purpose, Guid reference, CancellationToken ct)
    {
        var roles = await access.RolesAsync(ct);
        if (roles.Count == 0) return (null, false, false);
        var p = await ParentAsync(purpose, reference, ct);
        if (p == null || !await access.CampusAsync(p.CampusId, ct)) return (null, false, false);
        var owner = Owner(purpose, p, access.UserId, roles);
        var reviewer = Reviewer(purpose, p, access.UserId, roles);
        return owner || reviewer ? (p, owner, reviewer) : (null, false, false);
    }
    private static ResearchEvidenceFileDto Map(FileRow f) => new()
    {
        Reference = f.Reference, RevisionNo = f.RevisionNo, FileName = f.FileName,
        ContentType = f.ContentType, SizeBytes = f.SizeBytes, UploadedAtUtc = f.UploadedAtUtc
    };
    private const string FileColumns = """
        SELECT f.PublicId AS Reference, e.RevisionNo, f.OriginalFileName AS FileName,
               f.ContentType, f.SizeBytes, f.UploadedAt AS UploadedAtUtc,
               f.Id AS FileAssetId, f.StorageKey, f.Sha256, f.UploadedByUserId
        FROM dbo.ResearchEvidenceFiles AS e
        JOIN dbo.FileAssets AS f ON f.TenantId = e.TenantId AND f.Id = e.FileAssetId
        WHERE e.TenantId = {0} AND
        """;
    private const string FileGuard = """
        AND f.IsDeleted = 0 AND f.IsVerifiedSafe = 1 AND f.MalwareScanStateCode = N'Clean'
        AND f.Visibility = 1 AND f.StorageProvider = N'LocalPrivate'
        """;
    private static string ParentFilter(string purpose) => purpose == "proposal" ?
        " e.ProposalId = {1} AND e.AssessmentId IS NULL " :
        " e.AssessmentId = {1} AND e.ProposalId IS NULL ";

    public async Task<ApiResponse<PagedResult<ResearchEvidenceFileDto>>> ListAsync(
        string purpose, Guid parentReference, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!Purpose(purpose) || page is < 1 or > 100000 || pageSize is < 1 or > 50 ||
            page > int.MaxValue / pageSize)
            return ApiResponse<PagedResult<ResearchEvidenceFileDto>>.ErrorResponse("Invalid evidence page.", 400);
        var scope = await AuthorizedAsync(purpose, parentReference, ct);
        var parent = scope.Parent;
        if (parent == null) return Denied<PagedResult<ResearchEvidenceFileDto>>();
        var filter = ParentFilter(purpose) +
            " AND ( {2} = 1 OR e.RevisionNo <= {3} ) " + FileGuard;
        var count = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS Value FROM dbo.ResearchEvidenceFiles AS e
            JOIN dbo.FileAssets AS f ON f.TenantId = e.TenantId AND f.Id = e.FileAssetId
            WHERE e.TenantId = {0} AND
            """ + filter, access.TenantId, parent.Id, scope.Owner ? 1 : 0,
            parent.CurrentRevision).SingleAsync(ct);
        var rows = await db.Database.SqlQueryRaw<FileRow>(FileColumns + filter +
            " ORDER BY e.RevisionNo DESC, e.Id DESC OFFSET {4} ROWS FETCH NEXT {5} ROWS ONLY",
            access.TenantId, parent.Id, scope.Owner ? 1 : 0, parent.CurrentRevision,
            (page - 1) * pageSize, pageSize).ToListAsync(ct);
        var files = rows.Where(f => TenantKey(f.StorageKey, access.TenantId, purpose))
            .Select(Map).ToList();
        return ApiResponse<PagedResult<ResearchEvidenceFileDto>>.SuccessResponse(new()
        { Page = page, PageSize = pageSize, TotalCount = count, Items = files });
    }

    public async Task<ApiResponse<ResearchEvidenceFileDto>> UploadAsync(string purpose,
        Guid parentReference, Guid clientRequestId, PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!Purpose(purpose) || parentReference == Guid.Empty || clientRequestId == Guid.Empty ||
            upload == null || upload.Content == null || upload.Length is < 1 or > MaxBytes ||
            string.IsNullOrWhiteSpace(upload.FileName) || upload.FileName.Length > 255 ||
            string.IsNullOrWhiteSpace(upload.ContentType) || upload.ContentType.Length > 150)
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Invalid file (5 MB maximum).", 400);
        var scope = await AuthorizedAsync(purpose, parentReference, ct);
        var parent = scope.Parent;
        if (parent == null || !scope.Owner) return Denied<ResearchEvidenceFileDto>();
        var fileName = Path.GetFileName(upload.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || !Supported(fileName, upload.ContentType))
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Only PDF, PNG, JPEG and WebP are supported.", 400);
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await upload.Content.ReadAsync(chunk.AsMemory(), ct)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                    return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("File exceeds 5 MB.", 413);
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }
        }
        catch (IOException) { return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("File could not be read.", 400); }
        if (buffer.Length != upload.Length)
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("File length changed.", 400);
        var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        var existing = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(f =>
            f.TenantId == access.TenantId && f.PublicId == clientRequestId, ct);
        if (existing != null)
        {
            var replay = await db.Database.SqlQueryRaw<FileRow>(FileColumns + ParentFilter(purpose) +
                " AND f.Id = {2} " + FileGuard, access.TenantId, parent.Id, existing.Id).ToListAsync(ct);
            var prior = replay.SingleOrDefault();
            return prior != null && prior.UploadedByUserId == access.UserId &&
                prior.Sha256 == hash && prior.SizeBytes == buffer.Length &&
                prior.FileName == fileName && prior.ContentType == upload.ContentType &&
                TenantKey(prior.StorageKey, access.TenantId, purpose)
                ? ApiResponse<ResearchEvidenceFileDto>.SuccessResponse(Map(prior))
                : Conflict<ResearchEvidenceFileDto>();
        }
        if (!EvidenceWritable(purpose, parent.StateCode))
            return Conflict<ResearchEvidenceFileDto>();
        var quota = await SubscriptionStorageQuotaPolicy.GetAsync(db, access.TenantId, Now, ct);
        if (!quota.CanAdd(buffer.Length))
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Storage quota exceeded.", 409);
        buffer.Position = 0;
        var form = new FormFile(buffer, 0, buffer.Length, "file", fileName)
        { Headers = new HeaderDictionary(), ContentType = upload.ContentType };
        if (!storage.ValidateFile(form))
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("File signature or format is invalid.", 400);
        FileUploadResult stored;
        try { stored = await storage.UploadPrivateAsync(form, Folder(purpose)); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Research private upload unavailable for tenant {Tenant}", access.TenantId);
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Private storage unavailable.", 503);
        }
        if (!stored.Success || !TenantKey(stored.FileUrl, access.TenantId, purpose))
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Private storage unavailable.", 503);
        var key = stored.FileUrl;
        var committed = false;
        FileAsset? asset = null;
        try
        {
            var verifiedHash = await storage.ScanPrivateForMalwareAsync(key, ct);
            if (!string.Equals(verifiedHash, hash, StringComparison.Ordinal))
                return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Malware scan failed; upload rejected.", 503);
            asset = new FileAsset
            {
                TenantId = access.TenantId, PublicId = clientRequestId,
                StorageProvider = "LocalPrivate", StorageKey = key,
                OriginalFileName = fileName, ContentType = upload.ContentType,
                SizeBytes = buffer.Length, Sha256 = hash, UploadedByUserId = access.UserId,
                UploadedAt = Now, MalwareScanStateCode = "Clean", MalwareScanCompletedAt = Now,
                Visibility = FileVisibility.Private, IsVerifiedSafe = true, CreatedBy = access.UserId
            };
            var response = await db.ExecuteInTransactionAsync(async token =>
            {
                var current = await AuthorizedAsync(purpose, parentReference, token);
                var live = current.Parent;
                if (live == null || !current.Owner ||
                    live.Id != parent.Id || live.CurrentRevision != parent.CurrentRevision ||
                    !EvidenceWritable(purpose, live.StateCode))
                    return Conflict<ResearchEvidenceFileDto>();
                var revision = live.CurrentRevision + 1;
                var typeFilter = ParentFilter(purpose);
                var count = await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM dbo.ResearchEvidenceFiles AS e WHERE e.TenantId = {0} AND " +
                    typeFilter + " AND e.RevisionNo = {2}",
                    access.TenantId, live.Id, revision).SingleAsync(token);
                if (count >= MaxFilesPerRevision)
                    return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Maximum five files per evidence version.", 409);
                var currentQuota = await SubscriptionStorageQuotaPolicy.GetAsync(db, access.TenantId, Now, token);
                if (!currentQuota.CanAdd(asset.SizeBytes))
                    return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Storage quota exceeded.", 409);
                db.FileAssets.Add(asset);
                await db.SaveChangesAsync(token);
                var proposalId = purpose == "proposal" ? (long?)live.Id : null;
                var assessmentId = purpose == "accreditation" ? (long?)live.Id : null;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.ResearchEvidenceFiles
                      (TenantId, ProposalId, AssessmentId, RevisionNo, FileAssetId,
                       AttachedByUserId, AttachedAtUtc)
                    VALUES ({access.TenantId}, {proposalId}, {assessmentId}, {revision},
                            {asset.Id}, {access.UserId}, {Now})
                    """, token);
                return ApiResponse<ResearchEvidenceFileDto>.SuccessResponse(new()
                {
                    Reference = clientRequestId, RevisionNo = revision, FileName = fileName,
                    ContentType = upload.ContentType, SizeBytes = asset.SizeBytes, UploadedAtUtc = asset.UploadedAt
                });
            }, IsolationLevel.Serializable, ct);
            committed = response.Success;
            return response;
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Research evidence unique-file conflict for tenant {Tenant}", access.TenantId);
            return Conflict<ResearchEvidenceFileDto>();
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 1205 or 1222)
        {
            logger.LogWarning(ex, "Research evidence concurrent upload conflict for tenant {Tenant}", access.TenantId);
            return Conflict<ResearchEvidenceFileDto>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Research evidence upload failed for tenant {Tenant}", access.TenantId);
            return ApiResponse<ResearchEvidenceFileDto>.ErrorResponse("Evidence was not saved.", 503);
        }
        finally
        {
            if (!committed)
            {
                if (asset != null) db.Entry(asset).State = EntityState.Detached;
                try
                {
                    // A committed upload must never be deleted after a lost network response.
                    var persisted = await db.FileAssets.AsNoTracking().AnyAsync(f =>
                        f.TenantId == access.TenantId && f.PublicId == clientRequestId &&
                        f.StorageKey == key, CancellationToken.None);
                    if (!persisted && !await storage.DeletePrivateAsync(key))
                        logger.LogWarning("Orphan research file cleanup deferred for tenant {Tenant}", access.TenantId);
                }
                catch (Exception ex)
                { logger.LogError(ex, "Research file cleanup deferred for tenant {Tenant}", access.TenantId); }
            }
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(string purpose,
        Guid parentReference, Guid fileReference, CancellationToken ct = default)
    {
        if (!Purpose(purpose) || parentReference == Guid.Empty || fileReference == Guid.Empty)
            return Denied<DocumentFileDownloadDto>();
        var scope = await AuthorizedAsync(purpose, parentReference, ct);
        var parent = scope.Parent;
        if (parent == null) return Denied<DocumentFileDownloadDto>();
        var rows = await db.Database.SqlQueryRaw<FileRow>(FileColumns + ParentFilter(purpose) +
            " AND f.PublicId = {2} AND ({3} = 1 OR e.RevisionNo <= {4}) " + FileGuard,
            access.TenantId, parent.Id, fileReference, scope.Owner ? 1 : 0,
            parent.CurrentRevision).ToListAsync(ct);
        var file = rows.SingleOrDefault();
        if (file == null || !TenantKey(file.StorageKey, access.TenantId, purpose))
            return Denied<DocumentFileDownloadDto>();
        FileDownloadResult? stored;
        try { stored = await storage.GetPrivateFileAsync(file.StorageKey); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Research evidence read unavailable for tenant {Tenant}", access.TenantId);
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Evidence storage unavailable.", 503);
        }
        if (stored == null || stored.Content.LongLength != file.SizeBytes ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)), file.Sha256, StringComparison.Ordinal))
            return ApiResponse<DocumentFileDownloadDto>.ErrorResponse("Evidence file integrity check failed.", 409);
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new()
        {
            Content = stored.Content, ContentType = file.ContentType,
            FileName = Path.GetFileName(file.FileName.Replace('\\', '/'))
        });
    }

    private sealed class ParentRow
    {
        public long Id { get; set; }
        public long CampusId { get; set; }
        public long OwnerUserId { get; set; }
        public long? SupervisorUserId { get; set; }
        public long? ReviewerUserId { get; set; }
        public int CurrentRevision { get; set; }
        public string StateCode { get; set; } = "";
    }
    private sealed class FileRow
    {
        public Guid Reference { get; set; }
        public long FileAssetId { get; set; }
        public int RevisionNo { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }
        public string StorageKey { get; set; } = "";
        public string? Sha256 { get; set; }
        public long? UploadedByUserId { get; set; }
        public DateTime UploadedAtUtc { get; set; }
    }
}
