using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant administrator readback of canonical historical enrollment records.</summary>
public interface IStudentEnrollmentHistoryQueryService
{
    Task<ApiResponse<PagedResult<StudentEnrollmentHistoryRowDto>>> GetAsync(
        Guid studentReference, int page = 1, int pageSize = 20, CancellationToken ct = default);
}
