using EduOS.Core.Common;
using EduOS.Core.DTOs.Portals;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Read-only self-service projections. Server must validate parent/student membership and allowed publication visibility.</summary>
public interface ISelfServicePortalService
{
    Task<ApiResponse<IReadOnlyList<PortalStudentDto>>> GetLinkedStudentsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<PortalTimetableEntryDto>>> GetTimetableAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PortalAttendanceDto>>> GetAttendanceAsync(Guid studentReference, DateOnly fromDate, DateOnly toDate, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PortalResultDto>>> GetResultsAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PortalFeeLedgerDto>> GetFeesAsync(Guid studentReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PortalInvoiceDto>>> GetInvoicesAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PortalPaymentDto>>> GetPaymentsAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PortalTransportDto>>> GetTransportAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PortalHomeworkDto>>> GetHomeworkAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PortalAssignmentDto>>> GetAssignmentsAsync(Guid studentReference, int page, int pageSize, CancellationToken cancellationToken = default);
}
