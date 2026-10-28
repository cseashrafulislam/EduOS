using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Helpers.Storage;
using EduOS.Service.Services.Documents;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.HR;

public sealed partial class EmployeeContractService
{
    private const string SignedPdfTypeCode = "EMPLOYEE_CONTRACT_SIGNED_PDF";
    private IFileUploadService? privateStorage;

    public EmployeeContractService(EduOS.Persistence.Context.EduOSDbContext db,
        EduOS.Core.Interfaces.ICurrentUserService user, TimeProvider clock,
        IFileUploadService storage) : this(db, user, clock)
    { privateStorage = storage; }

    private SignedEmployeeContractPdfDto MapSignedPdf(Document document, FileAsset file) => new()
    {
        Reference = document.PublicId, FileReference = file.PublicId,
        FileName = file.OriginalFileName, Sha256 = file.Sha256 ?? string.Empty,
        Status = document.VerificationStatusCode, VerifiedAt = document.VerifiedAt,
        VerificationNote = document.VerificationNote,
        RowVersion = Convert.ToBase64String(document.RowVersion),
        CanVerify = document.IsActive && document.VerificationStatusCode == "Pending" &&
            document.CreatedBy.HasValue && document.CreatedBy != user.UserId
    };

    private AuditLog SignedPdfAudit(Document doc, string action) => new()
    {
        TenantId = user.TenantId, UserId = user.UserId, Action = action,
        EntityName = "EmployeeContractSignedPdf", EntityId = doc.Id,
        CorrelationId = doc.PublicId.ToString("D"), NewValue = doc.VerificationStatusCode,
        IsSuccess = true, OccurredAt = clock.GetUtcNow().UtcDateTime
    };

    private bool CleanPdf(FileAsset file) =>
        file.TenantId == user.TenantId && !file.IsDeleted && file.IsVerifiedSafe &&
        file.MalwareScanStateCode == "Clean" && file.Visibility == FileVisibility.Private &&
        file.StorageProvider == "LocalPrivate" &&
        DocumentFileService.IsTenantDocumentKey(file.StorageKey, user.TenantId) &&
        file.ContentType == "application/pdf" &&
        Path.GetExtension(file.OriginalFileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase) &&
        file.SizeBytes is > 8 and <= 5242880 && file.Sha256?.Length == 64;

    private async Task<ApiResponse<DocumentFileDownloadDto>> ReadPdfAsync(FileAsset file)
    {
        if (privateStorage == null) return Error<DocumentFileDownloadDto>("Private document storage unavailable.", 503);
        if (!CleanPdf(file)) return Error<DocumentFileDownloadDto>("Signed document is not security-cleared.", 409);
        FileDownloadResult? read;
        try { read = await privateStorage.GetPrivateFileAsync(file.StorageKey); }
        catch (IOException) { return Error<DocumentFileDownloadDto>("Private document unavailable.", 503); }
        catch (UnauthorizedAccessException) { return Error<DocumentFileDownloadDto>("Private document unavailable.", 503); }
        if (read?.Content == null || read.Content.LongLength != file.SizeBytes ||
            !read.Content.AsSpan().StartsWith("%PDF-"u8) ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(read.Content)), file.Sha256,
                StringComparison.Ordinal))
            return Error<DocumentFileDownloadDto>("Signed PDF integrity verification failed.", 409);
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = read.Content, ContentType = "application/pdf",
            FileName = Path.GetFileName(file.OriginalFileName.Replace('\\', '/'))
        });
    }

    public async Task<ApiResponse<SignedEmployeeContractPdfDto>> AttachSignedPdfAsync(Guid contractReference,
        AttachSignedEmployeeContractPdfRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<SignedEmployeeContractPdfDto>("Tenant admin required.", 403);
        if (contractReference == Guid.Empty || request == null || request.FileReference == Guid.Empty)
            return Error<SignedEmployeeContractPdfDto>("Contract and verified PDF reference required.");
        if (privateStorage == null) return Error<SignedEmployeeContractPdfDto>("Private document storage unavailable.", 503);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var contract = await db.EmployeeContracts.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == contractReference && !x.IsDeleted &&
                x.State == EmployeeContractState.Approved, ct);
            if (contract == null) return Error<SignedEmployeeContractPdfDto>("Approved contract not found.", 404);
            var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == request.FileReference && !x.IsDeleted, ct);
            if (asset == null || !CleanPdf(asset) || asset.UploadedByUserId != user.UserId)
                return Error<SignedEmployeeContractPdfDto>("PDF must be uploaded and security-scanned by this administrator.", 409);
            var existing = await db.Documents.AsNoTracking().Where(x => x.TenantId == user.TenantId &&
                x.EntityType == "EmployeeContract" && x.EntityId == contract.Id &&
                x.DocumentTypeCode == SignedPdfTypeCode && !x.IsDeleted).ToListAsync(ct);
            if (existing.Any(x => x.IsActive))
            {
                var current = existing.SingleOrDefault(x => x.IsActive);
                if (current != null && current.FileAssetId == asset.Id &&
                    current.VerificationStatusCode == "Pending")
                    return ApiResponse<SignedEmployeeContractPdfDto>.SuccessResponse(MapSignedPdf(current, asset));
                return Error<SignedEmployeeContractPdfDto>("Contract already has an active signed PDF.", 409);
            }
            if (await db.Documents.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId &&
                x.FileAssetId == asset.Id && !x.IsDeleted, ct))
                return Error<SignedEmployeeContractPdfDto>("PDF belongs to another document.", 409);
            var read = await ReadPdfAsync(asset);
            if (!read.Success) return Error<SignedEmployeeContractPdfDto>(read.Message, read.StatusCode);
            var type = await db.DocumentTypeDefinitions.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Code == SignedPdfTypeCode, ct);
            if (type != null && (!type.IsActive || type.IsDeleted ||
                !type.IsSensitive || !type.RequiresVerification))
                return Error<SignedEmployeeContractPdfDto>("Signed contract document classification is not secure.", 409);
            if (type == null)
                db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
                {
                    TenantId = user.TenantId, Code = SignedPdfTypeCode,
                    Name = "Signed employee contract (manual PDF)",
                    Description = "Restricted HR evidence; independent document verification required.",
                    IsActive = true, IsSensitive = true, RequiresVerification = true,
                    CreatedBy = user.UserId, CreatedAt = clock.GetUtcNow().UtcDateTime
                });
            var doc = new Document
            {
                TenantId = user.TenantId, PublicId = Guid.NewGuid(), FileAssetId = asset.Id,
                EntityType = "EmployeeContract", EntityId = contract.Id,
                DocumentTypeCode = SignedPdfTypeCode, Title = contract.ContractCode,
                VerificationStatusCode = "Pending", IsActive = true,
                CreatedAt = clock.GetUtcNow().UtcDateTime, CreatedBy = user.UserId
            };
            db.Documents.Add(doc);
            db.AuditLogs.Add(SignedPdfAudit(doc, "EmployeeContract.SignedPdfAttached"));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<SignedEmployeeContractPdfDto>.SuccessResponse(MapSignedPdf(doc, asset));
        }
        catch (DbUpdateConcurrencyException) { return Error<SignedEmployeeContractPdfDto>("Contract evidence changed concurrently.", 409); }
        catch (DbUpdateException) { return Error<SignedEmployeeContractPdfDto>("Contract evidence conflict; reload.", 409); }
    }

    public async Task<ApiResponse<SignedEmployeeContractPdfDto>> VerifySignedPdfAsync(Guid contractReference,
        VerifySignedEmployeeContractPdfRequestDto request, bool approve, CancellationToken ct = default)
    {
        if (!Allowed) return Error<SignedEmployeeContractPdfDto>("Tenant admin required.", 403);
        if (contractReference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256 ||
            request.Note?.Length > 500 || !approve && string.IsNullOrWhiteSpace(request.Note))
            return Error<SignedEmployeeContractPdfDto>("Valid review version and rejection reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var record = await (from d in db.Documents
                join c in db.EmployeeContracts.AsNoTracking() on new { d.TenantId, Id = d.EntityId }
                    equals new { c.TenantId, Id = (long?)c.Id }
                join f in db.FileAssets.AsNoTracking() on new { d.TenantId, Id = d.FileAssetId }
                    equals new { f.TenantId, f.Id }
                where d.TenantId == user.TenantId && c.PublicId == contractReference &&
                    c.State == EmployeeContractState.Approved && !c.IsDeleted &&
                    d.EntityType == "EmployeeContract" && d.DocumentTypeCode == SignedPdfTypeCode &&
                    d.IsActive && !d.IsDeleted
                select new { Document = d, File = f }).SingleOrDefaultAsync(ct);
            if (record == null) return Error<SignedEmployeeContractPdfDto>("Signed PDF not found.", 404);
            var doc = record.Document;
            if (doc.VerificationStatusCode != "Pending" || !Version(doc.RowVersion, request.RowVersion))
                return Error<SignedEmployeeContractPdfDto>("Signed PDF was reviewed or changed; reload.", 409);
            if (!doc.CreatedBy.HasValue || doc.CreatedBy == user.UserId)
                return Error<SignedEmployeeContractPdfDto>("Uploader cannot verify their own signed PDF.", 403);
            if (await db.Documents.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
                x.FileAssetId == record.File.Id && !x.IsDeleted, ct) != 1)
                return Error<SignedEmployeeContractPdfDto>("PDF attachment ownership conflict.", 409);
            var read = await ReadPdfAsync(record.File);
            if (!read.Success) return Error<SignedEmployeeContractPdfDto>(read.Message, read.StatusCode);
            doc.VerificationStatusCode = approve ? "Approved" : "Rejected";
            doc.IsActive = approve;
            doc.VerifiedAt = clock.GetUtcNow().UtcDateTime;
            doc.VerifiedByUserId = user.UserId;
            doc.VerificationNote = request.Note?.Trim();
            doc.UpdatedAt = doc.VerifiedAt; doc.UpdatedBy = user.UserId;
            db.AuditLogs.Add(SignedPdfAudit(doc, approve ?
                "EmployeeContract.SignedPdfApproved" : "EmployeeContract.SignedPdfRejected"));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<SignedEmployeeContractPdfDto>.SuccessResponse(MapSignedPdf(doc, record.File));
        }
        catch (DbUpdateConcurrencyException) { return Error<SignedEmployeeContractPdfDto>("Signed PDF changed concurrently.", 409); }
        catch (DbUpdateException) { return Error<SignedEmployeeContractPdfDto>("Signed PDF review conflict.", 409); }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadSignedPdfAsync(Guid contractReference,
        bool reviewPreview, CancellationToken ct = default)
    {
        if (!Allowed) return Error<DocumentFileDownloadDto>("Tenant admin required.", 403);
        if (contractReference == Guid.Empty) return Error<DocumentFileDownloadDto>("Contract reference required.");
        var record = await (from d in db.Documents.AsNoTracking()
            join c in db.EmployeeContracts.AsNoTracking() on new { d.TenantId, Id = d.EntityId }
                equals new { c.TenantId, Id = (long?)c.Id }
            join f in db.FileAssets.AsNoTracking() on new { d.TenantId, Id = d.FileAssetId }
                equals new { f.TenantId, f.Id }
            where d.TenantId == user.TenantId && c.PublicId == contractReference &&
                c.State == EmployeeContractState.Approved && !c.IsDeleted &&
                d.EntityType == "EmployeeContract" && d.DocumentTypeCode == SignedPdfTypeCode &&
                d.IsActive && !d.IsDeleted && !f.IsDeleted
            select new { Document = d, File = f }).SingleOrDefaultAsync(ct);
        if (record == null) return Error<DocumentFileDownloadDto>("Signed PDF not found.", 404);
        var doc = record.Document;
        if (reviewPreview)
        {
            if (doc.VerificationStatusCode != "Pending" || !doc.CreatedBy.HasValue ||
                doc.CreatedBy == user.UserId)
                return Error<DocumentFileDownloadDto>("Independent reviewer preview required.", 403);
        }
        else if (doc.VerificationStatusCode != "Approved" || !doc.VerifiedAt.HasValue ||
            !doc.VerifiedByUserId.HasValue || doc.VerifiedByUserId == doc.CreatedBy ||
            doc.UpdatedAt > doc.VerifiedAt)
            return Error<DocumentFileDownloadDto>("Approved signed PDF unavailable.", 404);
        if (await db.Documents.AsNoTracking().CountAsync(x => x.TenantId == user.TenantId &&
            x.FileAssetId == record.File.Id && !x.IsDeleted, ct) != 1)
            return Error<DocumentFileDownloadDto>("PDF attachment ownership conflict.", 409);
        var read = await ReadPdfAsync(record.File);
        if (!read.Success) return read;
        var audit = SignedPdfAudit(doc, reviewPreview ?
            "EmployeeContract.SignedPdfReviewViewed" : "EmployeeContract.SignedPdfDownloaded");
        db.AuditLogs.Add(audit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(audit).State = EntityState.Detached;
            return Error<DocumentFileDownloadDto>("Signed PDF audit unavailable.", 503);
        }
        return read;
    }
}
