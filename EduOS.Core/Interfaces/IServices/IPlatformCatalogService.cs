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
    Task<ApiResponse<IReadOnlyList<PlatformProductModuleAdminDto>>> GetAdminProductModulesAsync(CancellationToken ct = default);
    Task<ApiResponse<PlatformProductModuleAdminDto>> SaveProductModuleConfigurationAsync(long id, SavePlatformProductModuleRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<PlatformInstitutionModulePresetDto>> GetAdminInstitutionModulePresetAsync(long institutionTypeId, CancellationToken ct = default);
    Task<ApiResponse<PlatformInstitutionModuleRowDto>> SaveInstitutionModulePresetAsync(long institutionTypeId, long moduleId, SavePlatformInstitutionModulePresetDto request, CancellationToken ct = default);
    Task<ApiResponse<PlatformModuleFeatureDirectoryDto>> GetAdminModuleFeaturesAsync(long moduleId, CancellationToken ct = default);
    Task<ApiResponse<PlatformModuleFeatureRowDto>> SaveModuleFeatureMappingAsync(long moduleId, long featureId, SavePlatformModuleFeatureRequestDto request, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<PlatformFeatureAdminDto>>> GetAdminFeaturesAsync(CancellationToken ct = default);
    Task<ApiResponse<PlatformFeatureAdminDto>> SaveFeatureConfigurationAsync(long id, SavePlatformFeatureRequestDto request, CancellationToken ct = default);
}
