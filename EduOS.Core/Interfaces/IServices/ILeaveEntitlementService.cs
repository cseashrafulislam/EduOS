using EduOS.Core.Common;
using EduOS.Core.DTOs.Attendance;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Leave policy and auditable entitlement adjustments; balances are derived from entitlements, approved leave and adjustments.</summary>
public interface ILeaveEntitlementService
{
    Task<ApiResponse<IReadOnlyList<LeaveTypeDto>>> GetLeaveTypesAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<LeaveTypeDto>> SaveLeaveTypeAsync(long? leaveTypeId, SaveLeaveTypeRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<EmployeeLeaveEntitlementDto>>> GetEntitlementsAsync(Guid employeeReference, int year, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeLeaveEntitlementDto>> SaveEntitlementAsync(SaveEmployeeLeaveEntitlementRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeLeaveAdjustmentDto>> AddAdjustmentAsync(CreateEmployeeLeaveAdjustmentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<LeaveBalanceDto>>> GetBalancesAsync(Guid employeeReference, int year, CancellationToken cancellationToken = default);
}
