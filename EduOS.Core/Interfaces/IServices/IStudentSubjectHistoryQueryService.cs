using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentSubjectHistoryQueryService
{
    Task<ApiResponse<PagedResult<StudentSubjectHistoryRowDto>>> GetAsync(
        Guid studentReference, int page = 1, int pageSize = 20,
        CancellationToken ct = default);
}
