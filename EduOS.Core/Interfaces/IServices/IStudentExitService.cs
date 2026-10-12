using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.DTOs.Students;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentExitService
{
    Task<ApiResponse<StudentExitResultDto>> ProcessAsync(ProcessStudentExitDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<StudentExitResultDto>>> GetHistoryAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentTransferEligibilityDto>> GetTransferEligibilityAsync(Guid studentReference, CancellationToken cancellationToken = default);
}
