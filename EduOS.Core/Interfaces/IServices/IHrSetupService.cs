using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Tenant-owned HR master setup, separate from employee history and payroll.</summary>
public interface IHrSetupService
{
    Task<ApiResponse<PagedResult<OrganizationUnitDto>>> GetOrganizationUnitsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<OrganizationUnitDto>> SaveOrganizationUnitAsync(long? id, SaveOrganizationUnitRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<DesignationDto>>> GetDesignationsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<DesignationDto>> SaveDesignationAsync(long? id, SaveDesignationRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<WorkShiftDto>>> GetWorkShiftsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<WorkShiftDto>> SaveWorkShiftAsync(long? id, SaveWorkShiftRequestDto request, CancellationToken cancellationToken = default);
}
