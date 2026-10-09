using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Self-service and authorized review of student leave applications.</summary>
public interface IStudentLeaveService
{
    Task<ApiResponse<PagedResult<StudentLeaveApplicationDto>>> GetApplicationsAsync(Guid? enrollmentReference, LeaveState? state, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLeaveApplicationDto>> ApplyAsync(CreateStudentLeaveApplicationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StudentLeaveApplicationDto>> ReviewAsync(long applicationId, ReviewStudentLeaveRequestDto request, CancellationToken cancellationToken = default);
}
