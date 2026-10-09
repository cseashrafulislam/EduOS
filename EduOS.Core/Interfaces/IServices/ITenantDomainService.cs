using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Custom-domain ownership verification and tenant-scoped activation.</summary>
public interface ITenantDomainService
{
    Task<ApiResponse<IReadOnlyList<TenantDomainDto>>> GetDomainsAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDomainRegistrationDto>> RegisterDomainAsync(RegisterTenantDomainRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDomainDto>> VerifyDomainAsync(long domainId, ChangeTenantDomainRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDomainDto>> SetPrimaryAsync(long domainId, ChangeTenantDomainRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TenantDomainDto>> DeactivateAsync(long domainId, ChangeTenantDomainRequestDto request, CancellationToken cancellationToken = default);
}
