using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Transactional Tenant onboarding profile with verified owner and profile settings.</summary>
public interface ITenantOnboardingProfileService
{
    Task<ApiResponse<TenantOnboardingProfileDto>> GetAsync(CancellationToken ct = default);
    Task<ApiResponse<bool>> SaveAsync(TenantOnboardingProfileDto request, CancellationToken ct = default);
}
