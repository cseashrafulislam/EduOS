using EduOS.Core.Common;
using EduOS.Core.DTOs.Accounting;

namespace EduOS.Core.Interfaces.IServices;

public interface IInstitutionBudgetService
{
    Task<ApiResponse<PagedResult<InstitutionBudgetDto>>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<InstitutionBudgetDto>> GetAsync(long id, CancellationToken ct = default);
    Task<ApiResponse<InstitutionBudgetDto>> SubmitAsync(SubmitInstitutionBudgetRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<InstitutionBudgetDto>> ReviewAsync(long id, ReviewInstitutionBudgetRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<InstitutionBudgetActualDto>> ActualAsync(long id, CancellationToken ct = default);
}
