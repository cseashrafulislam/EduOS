using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;

namespace EduOS.Core.Interfaces.IServices;

public interface IEmployeeContractService
{
    Task<ApiResponse<PagedResult<EmployeeContractDto>>> ListAsync(Guid employeeReference, int page,
        int pageSize, CancellationToken ct = default);
    Task<ApiResponse<EmployeeContractDto>> CreateAsync(CreateEmployeeContractRequestDto request,
        CancellationToken ct = default);
    Task<ApiResponse<EmployeeContractDto>> ReviewAsync(Guid reference,
        ReviewEmployeeContractRequestDto request, bool approve, CancellationToken ct = default);
}
