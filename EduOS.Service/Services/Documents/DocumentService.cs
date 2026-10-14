using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Documents;

/// <summary>Tenant-scoped document metadata and configuration. Physical file operations remain private.</summary>
public sealed class DocumentService(EduOSDbContext db, ICurrentUserService user, ILogger<DocumentService> logger) : IDocumentService
{
    private bool CanManage => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    // Write the security audit entry in the same SaveChanges transaction as the business mutation.
    // Never persist file contents, document titles or template bodies in the audit payload.
    private AuditLog Audit(string action, string entityName, string reference, long? entityId, bool active)
    {
        var audit = new AuditLog
        {
            TenantId = user.TenantId, UserId = user.UserId, Action = action,
            EntityName = entityName, EntityId = entityId, CorrelationId = reference,
            NewValue = active ? "Active" : "Inactive", IsSuccess = true,
            OccurredAt = DateTime.UtcNow
        };
        db.AuditLogs.Add(audit);
        return audit;
    }
    // Failed EF writes leave Added and Modified entries tracked. Explicitly detach
    // only the two entities belonging to this operation, not unrelated scoped work.
    private void DiscardFailedWrite<TEntity>(TEntity? entity, AuditLog? audit) where TEntity : class
    {
        // A failed SaveChanges must not leak a rejected mutation or a success audit
        // into the next operation sharing this scoped DbContext.
        if (audit != null) db.Entry(audit).State = EntityState.Detached;
        if (entity != null) db.Entry(entity).State = EntityState.Detached;
    }
    private void DiscardFailedDocumentWrite(Document? document, AuditLog? audit) =>
        DiscardFailedWrite(document, audit);
    private static bool VersionMatches(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static DocumentDto Map(Document row, Guid fileReference) => new()
    {
        Id = row.Id, Reference = row.PublicId, FileReference = fileReference,
        DocumentTypeCode = row.DocumentTypeCode, EntityType = row.EntityType, EntityId = row.EntityId,
        Title = row.Title, ExpiresAt = row.ExpiresAt, IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
    private static DocumentTemplateDto Map(DocumentTemplate row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name, TemplateContent = row.TemplateContent,
        Format = row.Format, IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
    private static DocumentTypeDefinitionDto Map(DocumentTypeDefinition row) => new()
    {
        Id = row.Id, Code = row.Code, Name = row.Name, Description = row.Description,
        RequiresVerification = row.RequiresVerification, IsSensitive = row.IsSensitive,
        IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };

    public async Task<ApiResponse<PagedResult<DocumentDto>>> GetDocumentsAsync(string? entityType, long? entityId,
        int page, int pageSize, CancellationToken ct = default, string? documentTypeCode = null, string? status = null, string? search = null)
    {
        if (!CanManage) return ApiResponse<PagedResult<DocumentDto>>.ErrorResponse("Tenant administrator required.", 403);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize ||
            entityType?.Length > 100 || entityId < 1 || documentTypeCode?.Length > 100 ||
            search?.Length > 100 ||
            (status != null && status is not ("Active" or "Inactive" or "Expired" or "ExpiringSoon")))
            return ApiResponse<PagedResult<DocumentDto>>.ErrorResponse("Invalid document filter.");
        var query = from d in db.Documents.AsNoTracking()
                    join f in db.FileAssets.AsNoTracking() on new { d.TenantId, Id = d.FileAssetId }
                        equals new { f.TenantId, f.Id }
                    where d.TenantId == user.TenantId && !d.IsDeleted && !f.IsDeleted &&
                        (d.EntityType == null || d.EntityType == "" || (d.EntityType == "Tenant" && d.EntityId == user.TenantId))
                    select new { Document = d, FileReference = f.PublicId };
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(x => x.Document.EntityType == entityType.Trim());
        if (entityId.HasValue) query = query.Where(x => x.Document.EntityId == entityId);
        if (!string.IsNullOrWhiteSpace(documentTypeCode))
        {
            var normalizedType = Normalize(documentTypeCode);
            query = query.Where(x => x.Document.DocumentTypeCode == normalizedType);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Document.Title != null && x.Document.Title.Contains(term));
        }
        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);
        query = status switch
        {
            "Active" => query.Where(x => x.Document.IsActive && (!x.Document.ExpiresAt.HasValue || x.Document.ExpiresAt > now)),
            "Inactive" => query.Where(x => !x.Document.IsActive),
            "Expired" => query.Where(x => x.Document.IsActive && x.Document.ExpiresAt.HasValue && x.Document.ExpiresAt <= now),
            "ExpiringSoon" => query.Where(x => x.Document.IsActive && x.Document.ExpiresAt.HasValue &&
                x.Document.ExpiresAt > now && x.Document.ExpiresAt <= soon),
            _ => query
        };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Document.CreatedAt).ThenByDescending(x => x.Document.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<DocumentDto>>.SuccessResponse(new PagedResult<DocumentDto>
        {
            Items = rows.Select(x => Map(x.Document, x.FileReference)).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<DocumentDto>> GetDocumentAsync(Guid documentReference, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<DocumentDto>.ErrorResponse("Tenant administrator required.", 403);
        if (documentReference == Guid.Empty) return ApiResponse<DocumentDto>.ErrorResponse("Invalid document reference.");
        var row = await (from d in db.Documents.AsNoTracking()
                         join f in db.FileAssets.AsNoTracking() on new { d.TenantId, Id = d.FileAssetId }
                             equals new { f.TenantId, f.Id }
                         where d.TenantId == user.TenantId && d.PublicId == documentReference && !d.IsDeleted && !f.IsDeleted &&
                             (d.EntityType == null || d.EntityType == "" || (d.EntityType == "Tenant" && d.EntityId == user.TenantId))
                         select new { Document = d, FileReference = f.PublicId }).FirstOrDefaultAsync(ct);
        return row == null ? ApiResponse<DocumentDto>.ErrorResponse("Document not found.", 404)
            : ApiResponse<DocumentDto>.SuccessResponse(Map(row.Document, row.FileReference));
    }

    public async Task<ApiResponse<DocumentDto>> SaveDocumentAsync(Guid? documentReference, SaveDocumentRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<DocumentDto>.ErrorResponse("Tenant administrator required.", 403);
        if (request == null || request.FileReference == Guid.Empty || string.IsNullOrWhiteSpace(request.DocumentTypeCode) ||
            request.DocumentTypeCode.Trim().Length > 100 || request.EntityType?.Length > 100 ||
            request.Title?.Length > 250 || request.EntityId < 1)
            return ApiResponse<DocumentDto>.ErrorResponse("Invalid document metadata.");
        // The shared document API does not authorize arbitrary module aggregate IDs.
        // Until a module-specific owner check exists, only institution-wide or the
        // authenticated tenant's own Tenant aggregate can be linked here.
        var entityType = request.EntityType?.Trim();
        if (!string.IsNullOrEmpty(entityType) && !string.Equals(entityType, "Tenant", StringComparison.OrdinalIgnoreCase))
            return ApiResponse<DocumentDto>.ErrorResponse("Module-specific document attachments require entity authorization.", 403);
        if (string.IsNullOrEmpty(entityType) && request.EntityId.HasValue)
            return ApiResponse<DocumentDto>.ErrorResponse("Entity ID requires a supported entity type.");
        if (!string.IsNullOrEmpty(entityType) && request.EntityId.HasValue && request.EntityId != user.TenantId)
            return ApiResponse<DocumentDto>.ErrorResponse("Institution document reference does not match the current tenant.", 403);
        entityType = string.IsNullOrEmpty(entityType) ? null : "Tenant";
        long? entityId = entityType == "Tenant" ? user.TenantId : null;
        var typeCode = Normalize(request.DocumentTypeCode);
        var type = await db.DocumentTypeDefinitions.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.Code == typeCode && x.IsActive && !x.IsDeleted, ct);
        if (type == null) return ApiResponse<DocumentDto>.ErrorResponse("Select an active document type.", 400);
        // A type marked RequiresVerification must not be published as active until
        // a separately audited verification workflow exists. Metadata can be saved inactive.
        if ((type.RequiresVerification || type.IsSensitive) && request.IsActive)
            return ApiResponse<DocumentDto>.ErrorResponse("This document type requires a protected verification workflow; save it inactive.", 409);
        // Only attach a file registered for this exact tenant and explicitly cleared by security scanning.
        var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == request.FileReference &&
            !x.IsDeleted && x.IsVerifiedSafe && x.Visibility == FileVisibility.Private &&
            x.StorageProvider == "LocalPrivate" && x.MalwareScanStateCode == "Clean", ct);
        if (asset == null) return ApiResponse<DocumentDto>.ErrorResponse("File not registered or security-verified for this institution.", 403);
        // The shared document administration workflow owns only the private document
        // namespace. A verified asset uploaded for another module is not attachable here.
        // Use the same strict tenant storage-key policy as upload, recovery and
        // download. Empty path segments or trailing separators must not produce
        // document metadata that cannot subsequently be downloaded.
        if (!DocumentFileService.IsTenantDocumentKey(asset.StorageKey, user.TenantId))
            return ApiResponse<DocumentDto>.ErrorResponse("File is outside the institution document namespace.", 403);
        Document? pendingDocument = null;
        AuditLog? pendingAudit = null;
        try
        {
            Document row;
            if (documentReference.HasValue)
            {
                row = await db.Documents.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
                    x.PublicId == documentReference && !x.IsDeleted, ct) ?? null!;
                if (row == null) return ApiResponse<DocumentDto>.ErrorResponse("Document not found.", 404);
                if ((!string.IsNullOrWhiteSpace(row.EntityType) && row.EntityType != "Tenant") ||
                    (row.EntityType == "Tenant" && row.EntityId != user.TenantId))
                    return ApiResponse<DocumentDto>.ErrorResponse("Entity-linked documents must be updated by their owning module.", 403);
                if (!VersionMatches(row.RowVersion, request.RowVersion))
                    return ApiResponse<DocumentDto>.ErrorResponse("Document changed. Reload and retry.", 409);
                // A protected document must never be reclassified as an ordinary type
                // or rebound to a different file through metadata update. Renewals
                // require a separately verified file and a new document record.
                if (row.DocumentTypeCode != typeCode || row.FileAssetId != asset.Id)
                    return ApiResponse<DocumentDto>.ErrorResponse("Document type and file attachment cannot be changed. Create a new document version.", 409);
                // Do not mutate a tracked row before file-alias conflict checks below.
                // Otherwise a rejected update can leak audit timestamps into a later save.
            }
            else
            {
                var existing = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && !x.IsDeleted && x.FileAssetId == asset.Id &&
                    x.DocumentTypeCode == typeCode && x.EntityType == entityType && x.EntityId == entityId, ct);
                if (existing != null)
                {
                    // Repeated POST with identical natural key returns the existing record, not a duplicate.
                    if (existing.Title == request.Title?.Trim() && existing.ExpiresAt == request.ExpiresAt && existing.IsActive == request.IsActive)
                        return ApiResponse<DocumentDto>.SuccessResponse(Map(existing, asset.PublicId));
                    return ApiResponse<DocumentDto>.ErrorResponse("This file is already attached to this record.", 409);
                }
                row = new Document { TenantId = user.TenantId, PublicId = Guid.NewGuid(), CreatedBy = user.UserId };
            }
            // One private asset must not be aliased to multiple documents or types.
            // Otherwise a second non-sensitive document could expose a protected file.
            if (await db.Documents.AsNoTracking().AnyAsync(d => d.TenantId == user.TenantId &&
                !d.IsDeleted && d.FileAssetId == asset.Id && d.PublicId != row.PublicId, ct))
                return ApiResponse<DocumentDto>.ErrorResponse("File is already attached to another document.", 409);
            // Do not track a new document until all conflict checks pass. A rejected
            // request must not leak an Added entity into a later SaveChanges call.
            pendingDocument = row;
            if (!documentReference.HasValue) db.Documents.Add(row);
            else { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId; }
            var action = documentReference.HasValue ? "Document.Updated" : "Document.Created";
            row.FileAssetId = asset.Id;
            row.DocumentTypeCode = typeCode;
            row.EntityType = entityType;
            row.EntityId = entityId;
            row.Title = request.Title?.Trim();
            row.ExpiresAt = request.ExpiresAt;
            row.IsActive = request.IsActive;
            pendingAudit = Audit(action, "Document", row.PublicId.ToString("D"), documentReference.HasValue ? row.Id : null, row.IsActive);
            await db.SaveChangesAsync(ct);
            return ApiResponse<DocumentDto>.SuccessResponse(Map(row, asset.PublicId));
        }
        catch (DbUpdateConcurrencyException)
        {
            DiscardFailedDocumentWrite(pendingDocument, pendingAudit);
            return ApiResponse<DocumentDto>.ErrorResponse("Document changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            DiscardFailedDocumentWrite(pendingDocument, pendingAudit);
            logger.LogWarning(ex, "Document save conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentDto>.ErrorResponse("Document could not be saved due to a data conflict.", 409);
        }
    }

    public async Task<ApiResponse<DocumentDto>> DeactivateDocumentAsync(Guid documentReference, string rowVersion,
        CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<DocumentDto>.ErrorResponse("Tenant administrator required.", 403);
        var row = await db.Documents.FirstOrDefaultAsync(x => x.TenantId == user.TenantId &&
            x.PublicId == documentReference && !x.IsDeleted, ct);
        if (row == null) return ApiResponse<DocumentDto>.ErrorResponse("Document not found.", 404);
        if ((!string.IsNullOrWhiteSpace(row.EntityType) && row.EntityType != "Tenant") ||
            (row.EntityType == "Tenant" && row.EntityId != user.TenantId))
            return ApiResponse<DocumentDto>.ErrorResponse("Entity-linked documents must be managed by their owning module.", 403);
        if (!VersionMatches(row.RowVersion, rowVersion))
            return ApiResponse<DocumentDto>.ErrorResponse("Document changed. Reload and retry.", 409);
        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = user.UserId;
        var pendingAudit = Audit("Document.Deactivated", "Document", row.PublicId.ToString("D"), row.Id, false);
        try
        {
            await db.SaveChangesAsync(ct);
            var file = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == row.FileAssetId && !x.IsDeleted, ct);
            return file == null ? ApiResponse<DocumentDto>.ErrorResponse("Linked file no longer exists.", 409)
                : ApiResponse<DocumentDto>.SuccessResponse(Map(row, file.PublicId));
        }
        catch (DbUpdateConcurrencyException)
        {
            DiscardFailedWrite(row, pendingAudit);
            return ApiResponse<DocumentDto>.ErrorResponse("Document changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            DiscardFailedWrite(row, pendingAudit);
            logger.LogWarning(ex, "Document deactivation conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentDto>.ErrorResponse("Document deactivation conflict.", 409);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<DocumentTemplateDto>>> GetTemplatesAsync(CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<IReadOnlyList<DocumentTemplateDto>>.ErrorResponse("Tenant administrator required.", 403);
        var rows = await db.DocumentTemplates.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted)
            .OrderBy(x => x.Code).Take(500).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<DocumentTemplateDto>>.SuccessResponse(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<DocumentTemplateDto>> SaveTemplateAsync(long? templateId, SaveDocumentTemplateRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<DocumentTemplateDto>.ErrorResponse("Tenant administrator required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.TemplateContent) || request.TemplateContent.Length > 200000 ||
            request.Format is not ("HTML" or "TEXT"))
            return ApiResponse<DocumentTemplateDto>.ErrorResponse("Invalid template.");
        var code = Normalize(request.Code);
        var row = templateId.HasValue ? await db.DocumentTemplates.FirstOrDefaultAsync(x =>
            x.Id == templateId && x.TenantId == user.TenantId && !x.IsDeleted, ct) : null;
        if (templateId.HasValue && row == null) return ApiResponse<DocumentTemplateDto>.ErrorResponse("Template not found.", 404);
        if (row != null && !VersionMatches(row.RowVersion, request.RowVersion))
            return ApiResponse<DocumentTemplateDto>.ErrorResponse("Template changed. Reload and retry.", 409);
        if (row != null && row.Code != code) return ApiResponse<DocumentTemplateDto>.ErrorResponse("Template code cannot change.", 409);
        if (await db.DocumentTemplates.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            x.Code == code && (!templateId.HasValue || x.Id != templateId.Value), ct))
            return ApiResponse<DocumentTemplateDto>.ErrorResponse("Template code already exists.", 409);
        row ??= new DocumentTemplate { TenantId = user.TenantId, Code = code, CreatedBy = user.UserId };
        row.Name = request.Name.Trim();
        row.TemplateContent = request.TemplateContent;
        row.Format = request.Format;
        row.IsActive = request.IsActive;
        if (templateId.HasValue) { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId; }
        else db.DocumentTemplates.Add(row);
        var pendingAudit = Audit(templateId.HasValue ? "DocumentTemplate.Updated" : "DocumentTemplate.Created",
            "DocumentTemplate", code, templateId, row.IsActive);
        try { await db.SaveChangesAsync(ct); return ApiResponse<DocumentTemplateDto>.SuccessResponse(Map(row)); }
        catch (DbUpdateConcurrencyException)
        {
            DiscardFailedWrite(row, pendingAudit);
            return ApiResponse<DocumentTemplateDto>.ErrorResponse("Template changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            DiscardFailedWrite(row, pendingAudit);
            logger.LogWarning(ex, "Document template save conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentTemplateDto>.ErrorResponse("Template save conflict.", 409);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<DocumentTypeDefinitionDto>>> GetDocumentTypesAsync(CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<IReadOnlyList<DocumentTypeDefinitionDto>>.ErrorResponse("Tenant administrator required.", 403);
        var rows = await db.DocumentTypeDefinitions.AsNoTracking().Where(x => x.TenantId == user.TenantId && !x.IsDeleted)
            .OrderBy(x => x.Code).Take(500).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<DocumentTypeDefinitionDto>>.SuccessResponse(rows.Select(Map).ToList());
    }

    public async Task<ApiResponse<DocumentTypeDefinitionDto>> SaveDocumentTypeAsync(long? documentTypeId,
        SaveDocumentTypeDefinitionRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage) return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Tenant administrator required.", 403);
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 || request.Description?.Length > 1000)
            return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Invalid document type.");
        var code = Normalize(request.Code);
        var row = documentTypeId.HasValue ? await db.DocumentTypeDefinitions.FirstOrDefaultAsync(x =>
            x.Id == documentTypeId && x.TenantId == user.TenantId && !x.IsDeleted, ct) : null;
        if (documentTypeId.HasValue && row == null) return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Document type not found.", 404);
        if (row != null && !VersionMatches(row.RowVersion, request.RowVersion))
            return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Document type changed. Reload and retry.", 409);
        if (row != null && row.Code != code) return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Document type code cannot change.", 409);
        if (await db.DocumentTypeDefinitions.AsNoTracking().AnyAsync(x => x.TenantId == user.TenantId && !x.IsDeleted &&
            x.Code == code && (!documentTypeId.HasValue || x.Id != documentTypeId.Value), ct))
            return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Document type code already exists.", 409);
        // Never silently weaken verification/sensitivity for a type with existing
        // documents. Even inactive documents must retain their security policy.
        // Evaluate before mutating the tracked type or recording a success audit.
        if (row != null && ((row.IsSensitive && !request.IsSensitive) ||
            (row.RequiresVerification && !request.RequiresVerification)) &&
            await db.Documents.AsNoTracking().AnyAsync(d => d.TenantId == user.TenantId &&
                !d.IsDeleted && d.DocumentTypeCode == code, ct))
            return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse(
                "Document type security cannot be reduced while documents reference it.", 409);
        row ??= new DocumentTypeDefinition { TenantId = user.TenantId, Code = code, CreatedBy = user.UserId };
        row.Name = request.Name.Trim(); row.Description = request.Description?.Trim();
        row.RequiresVerification = request.RequiresVerification; row.IsSensitive = request.IsSensitive;
        row.IsActive = request.IsActive;
        if (documentTypeId.HasValue) { row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = user.UserId; }
        else db.DocumentTypeDefinitions.Add(row);
        var pendingAudit = Audit(documentTypeId.HasValue ? "DocumentType.Updated" : "DocumentType.Created",
            "DocumentTypeDefinition", code, documentTypeId, row.IsActive);
        try { await db.SaveChangesAsync(ct); return ApiResponse<DocumentTypeDefinitionDto>.SuccessResponse(Map(row)); }
        catch (DbUpdateConcurrencyException)
        {
            DiscardFailedWrite(row, pendingAudit);
            return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Document type changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            DiscardFailedWrite(row, pendingAudit);
            logger.LogWarning(ex, "Document type save conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<DocumentTypeDefinitionDto>.ErrorResponse("Document type save conflict.", 409);
        }
    }
}
