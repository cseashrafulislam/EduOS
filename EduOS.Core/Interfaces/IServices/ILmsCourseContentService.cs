using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.DTOs.Files;

namespace EduOS.Core.Interfaces.IServices;

public interface ILmsCourseContentService
{
    Task<ApiResponse<bool>> CanUploadResourceFileAsync(Guid lessonReference, CancellationToken ct = default);
    Task<ApiResponse<DocumentFileDownloadDto>> DownloadResourceFileAsync(long resourceId, CancellationToken ct = default);
    Task<ApiResponse<CourseContentDto>> GetCourseContentAsync(Guid courseReference, CancellationToken ct = default);
    Task<ApiResponse<CourseSectionDto>> SaveSectionAsync(long? sectionId, SaveCourseSectionRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<ContentLessonDto>> AssignLessonSectionAsync(AssignLessonSectionRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<LessonResourceDto>> SaveResourceAsync(long? resourceId, SaveLessonResourceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> RemoveResourceAsync(long resourceId, string rowVersion, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<AssignmentAttachmentDto>>> GetAssignmentAttachmentsAsync(Guid assignmentReference, CancellationToken ct = default);
    Task<ApiResponse<AssignmentAttachmentDto>> SaveAssignmentAttachmentAsync(long? id, SaveAssignmentAttachmentRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> RemoveAssignmentAttachmentAsync(long id, string rowVersion, CancellationToken ct = default);
}