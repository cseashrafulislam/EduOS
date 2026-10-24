using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface IPlatformCatalogService
{
    Task<ApiResponse<List<InstitutionTypeListItemDto>>> GetInstitutionTypesAsync();
    Task<ApiResponse<InstitutionTypeDetailDto>> GetInstitutionTypeByCodeAsync(string code);
    Task<ApiResponse<List<ProductModuleDto>>> GetModulesAsync();
    Task<ApiResponse<IReadOnlyList<PlatformInstitutionTypeAdminDto>>> GetAdminInstitutionTypesAsync(CancellationToken ct = default);
    Task<ApiResponse<PlatformInstitutionTypeAdminDto>> ChangeInstitutionTypeActivationAsync(long id, ChangeInstitutionTypeActivationRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<PlatformFeatureAdminDto>>> GetAdminFeaturesAsync(CancellationToken ct = default);
    Task<ApiResponse<PlatformFeatureAdminDto>> SaveFeatureConfigurationAsync(long id, SavePlatformFeatureRequestDto request, CancellationToken ct = default);
}
