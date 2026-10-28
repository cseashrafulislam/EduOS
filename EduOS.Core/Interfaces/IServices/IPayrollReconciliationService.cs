using EduOS.Core.Common;
using EduOS.Core.DTOs.Payroll;

namespace EduOS.Core.Interfaces.IServices;

public interface IPayrollReconciliationService
{
    Task<ApiResponse<PagedResult<PayrollReconciliationRowDto>>> GetAsync(
        int year, int month, long? campusId, int page, int pageSize,
        CancellationToken ct = default);
}
