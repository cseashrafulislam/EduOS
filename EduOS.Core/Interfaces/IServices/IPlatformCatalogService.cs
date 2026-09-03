using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;

namespace EduOS.Core.Interfaces.IServices;

public interface IPlatformCatalogService
{
    Task<ApiResponse<List<InstitutionTypeListItemDto>>> GetInstitutionTypesAsync();
    Task<ApiResponse<InstitutionTypeDetailDto>> GetInstitutionTypeByCodeAsync(string code);
    Task<ApiResponse<List<ProductModuleDto>>> GetModulesAsync();
}
