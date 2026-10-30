using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

public interface ILmsProgressGuardianService
{
    Task<ApiResponse<IReadOnlyList<LmsGuardianChoiceDto>>> EligibleAsync(long enrollmentId, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<LmsProgressGuardianGrantDto>>> GrantsAsync(long enrollmentId, CancellationToken ct = default);
    Task<ApiResponse<LmsProgressGuardianGrantDto>> GrantAsync(GrantLmsProgressGuardianRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<LmsProgressGuardianGrantDto>> RevokeAsync(Guid reference, RevokeLmsProgressGuardianRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<LmsGuardianCourseProgressDto>>> SharedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<LessonProgressDto>>> LessonsAsync(long enrollmentId, int page, int pageSize, CancellationToken ct = default);
}
