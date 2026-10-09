using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface ITenantModuleService
{
    Task<ApiResponse<List<TenantModuleDto>>> GetCurrentTenantModulesAsync();
    Task<ApiResponse<TenantModuleDto>> UpdateCurrentTenantModuleAsync(
        string moduleCode,
        UpdateTenantModuleRequestDto request);
    Task<ApiResponse<bool>> ValidateCurrentTenantSelectionAsync();
    Task<bool> IsCurrentTenantModuleAvailableAsync(string moduleCode);
    Task<ApiResponse<bool>> ApplyInstitutionPresetAsync(long tenantId, long institutionTypeDefinitionId, CancellationToken cancellationToken = default);
}
