using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
namespace EduOS.Core.Interfaces.IServices;
public interface IHrPayrollService
{
 Task<ApiResponse<bool>> SaveSalaryStructureAsync(SaveSalaryStructureDto request,CancellationToken cancellationToken=default);
 Task<ApiResponse<int>> SaveAttendanceAsync(SaveEmployeeAttendanceDto request,CancellationToken cancellationToken=default);
 Task<ApiResponse<PayrollBatchDto>> GeneratePayrollAsync(GeneratePayrollDto request,CancellationToken cancellationToken=default);
 Task<ApiResponse<PayrollRowDto>> PayAsync(PayPayrollDto request,CancellationToken cancellationToken=default);

 Task<ApiResponse<IReadOnlyList<PayrollEmployeeOptionDto>>> GetEmployeeOptionsAsync(string? search,CancellationToken cancellationToken=default);
 Task<ApiResponse<PayrollPeriodPageDto>> GetPeriodAsync(int year,int month,int page,int pageSize,CancellationToken cancellationToken=default);
 Task<ApiResponse<IReadOnlyList<PayrollRowDto>>> GetMyPayrollAsync(CancellationToken cancellationToken=default);
}
