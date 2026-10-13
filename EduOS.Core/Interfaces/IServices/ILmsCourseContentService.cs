using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

public interface ILmsCourseContentService
{
    Task<ApiResponse<CourseContentDto>> GetCourseContentAsync(Guid courseReference, CancellationToken ct = default);
    Task<ApiResponse<CourseSectionDto>> SaveSectionAsync(long? sectionId, SaveCourseSectionRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<ContentLessonDto>> AssignLessonSectionAsync(AssignLessonSectionRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<LessonResourceDto>> SaveResourceAsync(long? resourceId, SaveLessonResourceRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<bool>> RemoveResourceAsync(long resourceId, string rowVersion, CancellationToken ct = default);
}