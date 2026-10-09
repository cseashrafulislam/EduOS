using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Employee lifecycle, membership, protected bank-account records, and leave reviews.</summary>
public interface IHrAdminService
{
    Task<ApiResponse<PagedResult<HrEmployeeRowDto>>> GetEmployeesAsync(HrEmployeeQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDto>> GetEmployeeAsync(Guid employeeReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDto>> CreateEmployeeAsync(CreateEmployeeRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDto>> UpdateEmployeeAsync(Guid employeeReference, UpdateEmployeeRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeDto>> ChangeEmployeeStateAsync(Guid employeeReference, ChangeEmployeeStateRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeShiftAssignmentDto>> AssignShiftAsync(AssignEmployeeShiftRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeCampusAssignmentDto>> AssignCampusAsync(AssignEmployeeCampusRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<EmployeeAssignmentHistoryDto>>> GetAssignmentHistoryAsync(Guid employeeReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<IReadOnlyList<EmployeeBankAccountDto>>> GetBankAccountsAsync(Guid employeeReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeBankAccountDto>> SaveBankAccountAsync(long? bankAccountId, SaveEmployeeBankAccountRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<HrLeaveRowDto>>> GetEmployeeLeavesAsync(HrLeaveQueryDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> ReviewEmployeeLeaveAsync(ReviewEmployeeLeaveDto request, CancellationToken cancellationToken = default);
}
