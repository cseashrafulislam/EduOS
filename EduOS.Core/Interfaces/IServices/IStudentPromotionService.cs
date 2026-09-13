using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentPromotionService
{
    Task<ApiResponse<StudentPromotionResultDto>> PromoteAsync(
        Guid studentReference,
        PromoteStudentRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>> GetHistoryAsync(
        Guid studentReference,
        CancellationToken cancellationToken = default);
}
