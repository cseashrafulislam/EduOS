using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentExitService
{
    Task<ApiResponse<StudentExitResultDto>> ProcessAsync(ProcessStudentExitDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<StudentExitResultDto>>> GetHistoryAsync(Guid studentReference, CancellationToken cancellationToken = default);
}
