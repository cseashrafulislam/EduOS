using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;

namespace EduOS.Core.Interfaces.IServices;

public interface IPayrollAdministrationService
{
    Task<ApiResponse<IReadOnlyList<SalaryComponentDto>>> GetSalaryComponentsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<SalaryComponentDto>> SaveSalaryComponentAsync(long? componentId, SaveSalaryComponentRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<SalaryStructureDto>> SaveSalaryStructureAsync(SaveSalaryStructureRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PayrollRunDto>> CreatePayrollRunAsync(CreatePayrollRunRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PayrollRunDto>> GetPayrollRunAsync(Guid runReference, CancellationToken cancellationToken = default);
    Task<ApiResponse<PayrollRunDto>> ApprovePayrollRunAsync(Guid runReference, ApprovePayrollRunRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PayrollRunDto>> PostPayrollRunAsync(Guid runReference, PostPayrollRunRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<BonusDto>> SaveBonusAsync(long? bonusId, SaveBonusRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<LoanAdvanceDto>> CreateLoanAdvanceAsync(CreateLoanAdvanceRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PayrollRunDto>>> GetPayrollRunsAsync(int? year, int? month, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PayrollEmployeeDto>>> GetPayrollEmployeesAsync(Guid runReference, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<PayrollEmployeeDto>>> GetMyPayslipsAsync(int? year, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<PayrollPaymentDto>> RecordPayrollPaymentAsync(RecordPayrollPaymentRequestDto request, CancellationToken cancellationToken = default);
}
