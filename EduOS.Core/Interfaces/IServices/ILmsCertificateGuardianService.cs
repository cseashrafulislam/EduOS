using EduOS.Core.Common;
using EduOS.Core.DTOs.LMS;

namespace EduOS.Core.Interfaces.IServices;

public interface ILmsCertificateGuardianService
{
    Task<ApiResponse<IReadOnlyList<LmsGuardianChoiceDto>>> GetEligibleGuardiansAsync(Guid certificate, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<LmsCertificateGuardianGrantDto>>> GetActiveGrantsAsync(Guid certificate, CancellationToken ct = default);
    Task<ApiResponse<LmsCertificateGuardianGrantDto>> GrantAsync(GrantLmsCertificateGuardianRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<LmsCertificateGuardianGrantDto>> RevokeAsync(Guid grant, RevokeLmsCertificateGuardianRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<LmsCertificateDto>>> GetSharedCertificatesAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<LmsCertificateDto>> GetSharedCertificateAsync(Guid reference, CancellationToken ct = default);
}
