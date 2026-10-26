using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
namespace EduOS.Core.Interfaces.IServices;
public interface IStudentReadmissionService
{
    Task<ApiResponse<StudentReadmissionOptionsDto>> GetOptionsAsync(Guid studentReference, CancellationToken ct = default);
    Task<ApiResponse<StudentReadmissionResultDto>> ProcessAsync(Guid studentReference, ProcessStudentReadmissionDto request, CancellationToken ct = default);
}
