using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.Entities.Files;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Helpers.Storage;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.LMS;

public sealed partial class LmsCourseContentService
{
    // Other tenant-private assets (including student documents) must not become LMS handouts.
    private static bool IsLmsPrivateAsset(FileAsset? file, long tenantId, long? uploaderId = null)
    {
        if (file == null || file.TenantId != tenantId || file.IsDeleted ||
            !file.IsVerifiedSafe || file.MalwareScanStateCode != "Clean" ||
            file.Visibility != FileVisibility.Private || file.StorageProvider != "LocalPrivate" ||
            file.SizeBytes is < 1 or > 5242880 ||
            string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64 ||
            (uploaderId.HasValue && file.UploadedByUserId != uploaderId.Value))
            return false;
        var prefix = $"tenant-{tenantId}/lms/";
        var key = file.StorageKey;
        return key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length &&
            !key.Contains('\\') && !key.Contains("//") &&
            !key.Split('/').Any(segment => segment is "" or "." or "..");
    }

    public async Task<ApiResponse<FileAssetDto>> UploadResourceFileAsync(
        Guid lessonReference, Guid requestId, PrivateFileUploadDto upload, CancellationToken ct = default)
    {
        if (_documentFiles == null)
            return Fail<FileAssetDto>("Private file upload unavailable.", 503);
        var access = await CanUploadResourceFileAsync(lessonReference, ct);
        if (!access.Success)
            return Fail<FileAssetDto>(access.Message, access.StatusCode);
        return await _documentFiles.UploadForLmsAsync(requestId, upload, ct);
    }

    public async Task<ApiResponse<bool>> CanUploadResourceFileAsync(
        Guid lessonReference, CancellationToken ct = default)
    {
        if (!Author || lessonReference == Guid.Empty)
            return Fail<bool>("LMS author permission required.", 403);
        var lesson = await _lessons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.PublicId == lessonReference && !x.IsDeleted, ct);
        var course = lesson == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == lesson.CourseId && !x.IsDeleted, ct);
        if (course == null || !course.IsActive || !await CanEditAsync(course, ct))
            return Fail<bool>("Lesson author access denied.", 404);
        if (await CourseCompletionPublicationGuard.IsLockedAsync(_enrollments, Tenant, course.Id, ct))
            return Fail<bool>(CourseCompletionPublicationGuard.Message, 409);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadResourceFileAsync(
        long resourceId, CancellationToken ct = default)
    {
        if (!Authenticated || resourceId <= 0)
            return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
        if (_storage == null)
            return Fail<DocumentFileDownloadDto>("Private file storage unavailable.", 503);
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var resource = await _resources.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == resourceId && !x.IsDeleted && x.FileAssetId.HasValue, token);
            var lesson = resource == null ? null : await _lessons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == resource.LessonId && !x.IsDeleted, token);
            var course = lesson == null ? null : await _courses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == lesson.CourseId && !x.IsDeleted, token);
            if (course == null || !await CanViewAsync(course, token))
                return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
            var edit = await CanEditAsync(course, token);
            if (!edit && (!lesson!.IsPublished ||
                (lesson.CourseSectionId.HasValue && !await _sections.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.Id == lesson.CourseSectionId.Value &&
                    x.CourseId == course.Id && x.IsPublished && !x.IsDeleted, token))))
                return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
            var asset = await _files.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == resource!.FileAssetId!.Value, token);
            if (!IsLmsPrivateAsset(asset, Tenant))
                return Fail<DocumentFileDownloadDto>("Resource not found.", 404);
            FileDownloadResult? stored;
            try { stored = await _storage.GetPrivateFileAsync(asset!.StorageKey); }
            catch (IOException) { return Fail<DocumentFileDownloadDto>("Resource file unavailable.", 503); }
            if (stored == null || stored.Content.Length != asset!.SizeBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(stored.Content)), asset.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                return Fail<DocumentFileDownloadDto>("Resource file integrity check failed.", 409);
            return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
            {
                Content = stored.Content, ContentType = asset.ContentType,
                FileName = Path.GetFileName(asset.OriginalFileName)
            });
        }, IsolationLevel.Serializable, ct);
    }
}
