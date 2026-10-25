using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;

namespace EduOS.Core.Interfaces.IServices;

public interface IStudentLifecycleTimelineService
{
    Task<ApiResponse<StudentLifecycleTimelineDto>> GetAsync(Guid studentReference,
        CancellationToken cancellationToken = default);
}
