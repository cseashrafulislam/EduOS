using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentDirectoryService
{
    Task<ApiResponse<PagedResult<StudentDirectoryListItemDto>>> GetPageAsync(StudentDirectoryQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentDirectoryDetailsDto>> GetAsync(Guid reference, CancellationToken cancellationToken = default);
}
