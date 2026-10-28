using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;

namespace EduOS.Core.Interfaces.IServices;

public interface IPayrollSalaryStatementService
{
    Task<ApiResponse<PagedResult<PayrollSalaryStatementRowDto>>> GetAsync(
        int? year, int? month, int page, int pageSize, CancellationToken ct = default);
}
