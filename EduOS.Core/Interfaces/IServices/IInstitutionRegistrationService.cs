using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>
/// Institution registration only. Tenant profile, campus, academic setup and
/// onboarding state each have separate canonical service owners.
/// Public verification URLs must come from trusted server configuration.
/// </summary>
public interface IInstitutionRegistrationService
{
    Task<ApiResponse<InstitutionSignupResponseDto>> RegisterInstitutionAsync(InstitutionSignupRequestDto request, CancellationToken cancellationToken = default);
    Task<ApiResponse<bool>> VerifyEmailAsync(string email, string token, CancellationToken cancellationToken = default);
}
