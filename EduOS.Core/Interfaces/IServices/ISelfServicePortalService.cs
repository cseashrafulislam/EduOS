using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;

namespace EduOS.Core.Interfaces.IServices;

public interface ISelfServicePortalService
{
    Task<ApiResponse<IReadOnlyList<PortalStudentDto>>> GetLinkedStudentsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<PortalAttendanceDto>>> GetAttendanceAsync(Guid studentReference, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<PortalResultDto>>> GetResultsAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PortalFeeLedgerDto>> GetFeesAsync(Guid studentReference, CancellationToken cancellationToken = default);
}
