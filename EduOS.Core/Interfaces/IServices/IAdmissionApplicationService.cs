using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;

namespace EduOS.Core.Interfaces.IServices;

public interface IAdmissionApplicationService
{
    Task<ApiResponse<AdmissionApplicationOptionsDto>> GetOptionsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<PagedResult<AdmissionApplicationListItemDto>>> GetPageAsync(AdmissionApplicationQueryDto query, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionApplicationDetailsDto>> GetByReferenceAsync(Guid reference, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionApplicationCreatedDto>> CreateAsync(CreateAdmissionApplicationDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<AdmissionApplicationDetailsDto>> ReviewAsync(Guid reference, ReviewAdmissionApplicationDto request, CancellationToken cancellationToken = default);
}
