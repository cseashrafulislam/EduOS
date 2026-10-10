using EduOS.Core.Common;
using EduOS.Core.DTOs.Tenants;

namespace EduOS.Core.Interfaces.IServices;

/// <summary>Compatibility adapter for the onboarding profile form; delegates canonical tenant writes.</summary>
public interface IInstitutionProfileWizardService
{
    Task<ApiResponse<InstitutionProfileWizardDto>> GetAsync(CancellationToken ct = default);
    Task<ApiResponse<bool>> SaveAsync(InstitutionProfileWizardDto request, CancellationToken ct = default);
}
