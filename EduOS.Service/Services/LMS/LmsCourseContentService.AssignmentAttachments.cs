using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.LMS;

/// <summary>Assignment file references extend the canonical LMS assignment aggregate.</summary>
public sealed partial class LmsCourseContentService
{
    private static AssignmentAttachmentDto AttachmentRow(AssignmentAttachment row, Guid reference, FileAsset file) => new()
    {
        Id = row.Id, AssignmentReference = reference, FileAssetReference = file.PublicId,
        FileName = file.OriginalFileName, Title = row.Title,
        FileDownloadable = IsLmsPrivateAsset(file, row.TenantId),
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    public async Task<ApiResponse<FileAssetDto>> UploadAssignmentAttachmentFileAsync(Guid assignmentReference,
        Guid requestId, PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (!Author || assignmentReference == Guid.Empty || requestId == Guid.Empty)
            return Fail<FileAssetDto>("Assignment author permission required.", 403);
        if (_documentFiles == null) return Fail<FileAssetDto>("Private file upload unavailable.", 503);
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == assignmentReference && !x.IsDeleted, ct);
        var course = assignment == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (course == null || !course.IsActive || !await CanEditAsync(course, ct))
            return Fail<FileAssetDto>("Assignment author access denied.", 404);
        if (assignment!.IsPublished && !await HasOfferingAsync(course, null, null, ct))
            return Fail<FileAssetDto>("Active curriculum offering required.", 409);
        if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, ct))
            return Fail<FileAssetDto>(CourseCompletionPublicationGuard.Message, 409);
        if (await _attachments.GetQueryable().AsNoTracking().CountAsync(x =>
            x.TenantId == Tenant && x.AssignmentId == assignment.Id && !x.IsDeleted, ct) >= 20)
            return Fail<FileAssetDto>("Maximum 20 attachments per assignment.", 409);
        // Existing private LMS scan, quota and idempotent upload pipeline.
        // Saving the link independently revalidates author, tenant and locked state.
        return await _documentFiles.UploadForLmsAsync(requestId, upload, ct);
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAssignmentAttachmentAsync(
        long attachmentId, CancellationToken ct = default)
    {
        if (!Authenticated || attachmentId <= 0) return Fail<DocumentFileDownloadDto>("Attachment not found.", 404);
        if (_storage == null) return Fail<DocumentFileDownloadDto>("Private file storage unavailable.", 503);
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var link = await _attachments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == attachmentId && !x.IsDeleted, token);
            var assignment = link == null ? null : await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == link.AssignmentId && !x.IsDeleted, token);
            var course = assignment == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == assignment.CourseId && !x.IsDeleted, token);
            if (course == null || !await CanViewAsync(course, token))
                return Fail<DocumentFileDownloadDto>("Attachment not found.", 404);
            var editor = await CanEditAsync(course, token);
            if (!editor && (!course.IsActive || !assignment!.IsPublished ||
                (assignment.OpensAt.HasValue && assignment.OpensAt.Value > Now)))
                return Fail<DocumentFileDownloadDto>("Attachment not found.", 404);
            var asset = await _files.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == link!.FileAssetId && !x.IsDeleted, token);
            if (!IsLmsPrivateAsset(asset, Tenant))
                return Fail<DocumentFileDownloadDto>("Attachment not found.", 404);
            EduOS.Service.Helpers.Storage.FileDownloadResult? stored;
            try { stored = await _storage.GetPrivateFileAsync(asset!.StorageKey); }
            catch (IOException) { return Fail<DocumentFileDownloadDto>("Attachment unavailable.", 503); }
            catch (UnauthorizedAccessException) { return Fail<DocumentFileDownloadDto>("Attachment unavailable.", 503); }
            if (stored == null || stored.Content.Length != asset!.SizeBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)), asset.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                return Fail<DocumentFileDownloadDto>("Attachment integrity check failed.", 409);
            return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
            {
                Content = stored.Content, ContentType = asset.ContentType,
                FileName = Path.GetFileName(asset.OriginalFileName.Replace('\\', '/'))
            });
        }, IsolationLevel.Serializable, ct);
    }

    public async Task<ApiResponse<IReadOnlyList<AssignmentAttachmentDto>>> GetAssignmentAttachmentsAsync(
        Guid assignmentReference, CancellationToken ct = default)
    {
        if (!Authenticated || assignmentReference == Guid.Empty)
            return Fail<IReadOnlyList<AssignmentAttachmentDto>>("Assignment not found.", 404);
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == assignmentReference && !x.IsDeleted, ct);
        var course = assignment == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (course == null || !await CanViewAsync(course, ct))
            return Fail<IReadOnlyList<AssignmentAttachmentDto>>("Assignment not found.", 404);
        var editor = await CanEditAsync(course, ct);
        if (!editor && (!assignment!.IsPublished || !course.IsActive ||
            (assignment.OpensAt.HasValue && assignment.OpensAt.Value > Now)))
            return Fail<IReadOnlyList<AssignmentAttachmentDto>>("Assignment not available.", 404);
        var rows = await _attachments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.AssignmentId == assignment!.Id && !x.IsDeleted)
            .OrderBy(x => x.Id).Take(21).ToListAsync(ct);
        if (rows.Count > 20)
            return Fail<IReadOnlyList<AssignmentAttachmentDto>>("Assignment attachment limit exceeded.", 409);
        var ids = rows.Select(x => x.FileAssetId).Distinct().ToArray();
        var files = await _files.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == Tenant && ids.Contains(x.Id) && !x.IsDeleted && x.IsVerifiedSafe)
            .ToDictionaryAsync(x => x.Id, ct);
        IReadOnlyList<AssignmentAttachmentDto> result = rows.Where(x => files.ContainsKey(x.FileAssetId))
            .Select(x => AttachmentRow(x, assignment!.PublicId, files[x.FileAssetId])).ToArray();
        return ApiResponse<IReadOnlyList<AssignmentAttachmentDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<AssignmentAttachmentDto>> SaveAssignmentAttachmentAsync(
        long? id, SaveAssignmentAttachmentRequestDto request, CancellationToken ct = default)
    {
        if (!Author) return Fail<AssignmentAttachmentDto>("Author permission required.", 403);
        if (request == null || request.AssignmentReference == Guid.Empty ||
            request.FileAssetReference == Guid.Empty || id is <= 0 ||
            request.Title?.Length > 200)
            return Fail<AssignmentAttachmentDto>("Invalid assignment attachment fields.");
        var assignment = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == request.AssignmentReference && !x.IsDeleted, ct);
        var course = assignment == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == assignment.CourseId && !x.IsDeleted, ct);
        if (course == null || !await CanEditAsync(course, ct))
            return Fail<AssignmentAttachmentDto>("Course author access denied.", 403);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var current = await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == assignment!.Id && !x.IsDeleted, token);
                var currentCourse = await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == course.Id && !x.IsDeleted, token);
                if (current == null || currentCourse == null || !await CanEditAsync(currentCourse, token))
                    return Fail<AssignmentAttachmentDto>("Assignment author access changed.", 409);
                if (current.IsPublished && (!currentCourse.IsActive ||
                    !await HasOfferingAsync(currentCourse, null, null, token)))
                    return Fail<AssignmentAttachmentDto>("Active curriculum offering required for published assignments.", 409);
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, token))
                    return Fail<AssignmentAttachmentDto>(CourseCompletionPublicationGuard.Message, 409);
                var file = await _files.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == request.FileAssetReference &&
                    x.IsVerifiedSafe && !x.IsDeleted && x.UploadedByUserId == _user.UserId, token);
                if (file == null)
                    return Fail<AssignmentAttachmentDto>("A verified file uploaded by this user is required.", 409);
                var row = id.HasValue ? await _attachments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == id.Value && x.AssignmentId == current.Id && !x.IsDeleted, token) : null;
                if (id.HasValue && row == null) return Fail<AssignmentAttachmentDto>("Attachment not found.", 404);
                // Preserve legacy attachment title editing, but never let a new link
                // repurpose an unrelated private student/document file as an LMS handout.
                if (!IsLmsPrivateAsset(file, Tenant) && (row == null || row.FileAssetId != file.Id))
                    return Fail<AssignmentAttachmentDto>("A verified LMS upload is required.", 409);
                var title = Trim(request.Title);
                var duplicate = await _attachments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.AssignmentId == current.Id && x.FileAssetId == file.Id &&
                    !x.IsDeleted && (row == null || x.Id != row.Id), token);
                if (duplicate != null)
                {
                    if (row == null && duplicate.Title == title)
                        return ApiResponse<AssignmentAttachmentDto>.SuccessResponse(
                            AttachmentRow(duplicate, current.PublicId, file), "Attachment already linked.");
                    return Fail<AssignmentAttachmentDto>("This file is already linked to the assignment.", 409);
                }
                if (row != null && !Version(row.RowVersion, request.RowVersion))
                    return Fail<AssignmentAttachmentDto>("Attachment changed. Reload before editing.", 409);
                if (row == null)
                {
                    var count = await _attachments.GetQueryable().CountAsync(x =>
                        x.TenantId == Tenant && x.AssignmentId == current.Id && !x.IsDeleted, token);
                    if (count >= 20) return Fail<AssignmentAttachmentDto>("Maximum 20 attachments per assignment.", 409);
                    row = new AssignmentAttachment
                    {
                        TenantId = Tenant, AssignmentId = current.Id,
                        CreatedAt = Now, CreatedBy = _user.UserId
                    };
                    await _attachments.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = Now; row.UpdatedBy = _user.UserId; _attachments.Update(row);
                }
                row.FileAssetId = file.Id; row.Title = title;
                await _uow.SaveChangesAsync(token);
                return ApiResponse<AssignmentAttachmentDto>.SuccessResponse(
                    AttachmentRow(row, current.PublicId, file), "Assignment attachment saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<AssignmentAttachmentDto>("Attachment changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Assignment attachment conflict for tenant {Tenant}", Tenant);
            return Fail<AssignmentAttachmentDto>("Attachment save conflicts with another request.", 409);
        }
    }

    public async Task<ApiResponse<bool>> RemoveAssignmentAttachmentAsync(
        long id, string rowVersion, CancellationToken ct = default)
    {
        if (!Author) return Fail<bool>("Author permission required.", 403);
        if (id <= 0) return Fail<bool>("Invalid attachment.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var row = await _attachments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == id, token);
                var assignment = row == null ? null : await _assignments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.AssignmentId && !x.IsDeleted, token);
                var course = assignment == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == assignment.CourseId && !x.IsDeleted, token);
                if (row == null || course == null) return Fail<bool>("Attachment not found.", 404);
                if (!await CanEditAsync(course, token)) return Fail<bool>("Course author access denied.", 403);
                if (row.IsDeleted) return ApiResponse<bool>.SuccessResponse(true, "Attachment already removed.");
                if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, token))
                    return Fail<bool>(CourseCompletionPublicationGuard.Message, 409);
                if (!Version(row.RowVersion, rowVersion))
                    return Fail<bool>("Attachment changed. Reload before removing.", 409);
                row.IsDeleted = true; row.DeletedAt = Now; row.DeletedBy = _user.UserId;
                row.UpdatedAt = Now; row.UpdatedBy = _user.UserId;
                _attachments.Update(row);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true, "Attachment removed.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Fail<bool>("Attachment changed concurrently.", 409); }
    }
}
