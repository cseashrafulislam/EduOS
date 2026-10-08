using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.SaaS;

public class PlatformCatalogService : IPlatformCatalogService
{
    private readonly IGenericRepository<InstitutionTypeDefinition> _institutionTypeRepository;
    private readonly IGenericRepository<InstitutionTypeModule> _institutionTypeModuleRepository;
    private readonly IGenericRepository<ProductModule> _moduleRepository;
    private readonly ILogger<PlatformCatalogService> _logger;

    public PlatformCatalogService(
        IGenericRepository<InstitutionTypeDefinition> institutionTypeRepository,
        IGenericRepository<InstitutionTypeModule> institutionTypeModuleRepository,
        IGenericRepository<ProductModule> moduleRepository,
        ILogger<PlatformCatalogService> logger)
    {
        _institutionTypeRepository = institutionTypeRepository;
        _institutionTypeModuleRepository = institutionTypeModuleRepository;
        _moduleRepository = moduleRepository;
        _logger = logger;
    }

    public async Task<ApiResponse<List<InstitutionTypeListItemDto>>> GetInstitutionTypesAsync()
    {
        try
        {
            var types = await _institutionTypeRepository.GetQueryable()
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new InstitutionTypeListItemDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    NameBangla = x.NameBangla,
                    Description = x.Description,
                    AcademicCycleType = x.DefaultAcademicCycle.ToString(),
                    DisplayOrder = x.DisplayOrder
                }).ToListAsync();
            return ApiResponse<List<InstitutionTypeListItemDto>>.SuccessResponse(types);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load public institution types");
            return ApiResponse<List<InstitutionTypeListItemDto>>.ErrorResponse("Failed to load institution types", 500);
        }
    }

    public async Task<ApiResponse<InstitutionTypeDetailDto>> GetInstitutionTypeByCodeAsync(string code)
    {
        if (!TryNormalizeCode(code, out var normalizedCode))
            return ApiResponse<InstitutionTypeDetailDto>.ErrorResponse("Institution type code is invalid", 400);

        try
        {
            var institutionType = await _institutionTypeRepository.GetQueryable()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Code == normalizedCode && x.IsActive);
            if (institutionType == null)
                return ApiResponse<InstitutionTypeDetailDto>.ErrorResponse("Institution type not found", 404);

            var moduleLinks = await (
                from mapping in _institutionTypeModuleRepository.GetQueryable().AsNoTracking()
                join module in _moduleRepository.GetQueryable().AsNoTracking()
                    on mapping.ProductModuleId equals module.Id
                where mapping.InstitutionTypeDefinitionId == institutionType.Id && module.IsActive
                orderby module.DisplayOrder, module.Name
                select new { Module = module, Mapping = mapping }).ToListAsync();

            var dto = new InstitutionTypeDetailDto
            {
                Id = institutionType.Id,
                Code = institutionType.Code,
                Name = institutionType.Name,
                NameBangla = institutionType.NameBangla,
                Description = institutionType.Description,
                AcademicCycleType = institutionType.DefaultAcademicCycle.ToString(),
                DisplayOrder = institutionType.DisplayOrder,
                Terminology = new Dictionary<string, string>(),
                DefaultSettings = new Dictionary<string, string>(),
                Modules = moduleLinks.Select(x => MapModule(x.Module, x.Mapping)).ToList()
            };
            return ApiResponse<InstitutionTypeDetailDto>.SuccessResponse(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load institution type {Code}", normalizedCode);
            return ApiResponse<InstitutionTypeDetailDto>.ErrorResponse("Failed to load institution type", 500);
        }
    }

    public async Task<ApiResponse<List<ProductModuleDto>>> GetModulesAsync()
    {
        try
        {
            var modules = await _moduleRepository.GetQueryable().AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new ProductModuleDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    Description = x.Description,
                    IsCore = x.IsCore,
                    IsActive = x.IsActive,
                    DisplayOrder = x.DisplayOrder
                }).ToListAsync();
            return ApiResponse<List<ProductModuleDto>>.SuccessResponse(modules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load product modules");
            return ApiResponse<List<ProductModuleDto>>.ErrorResponse("Failed to load modules", 500);
        }
    }

    private static InstitutionTypeModuleDto MapModule(ProductModule module, InstitutionTypeModule mapping)
    {
        return new InstitutionTypeModuleDto
        {
            Id = module.Id,
            Code = module.Code,
            Name = module.Name,
            Description = module.Description,
            IsCore = module.IsCore,
            IsActive = module.IsActive,
            DisplayOrder = module.DisplayOrder,
            IsRequired = mapping.IsRequired,
            IsEnabledByDefault = mapping.IsDefaultEnabled
        };
    }

    private static bool TryNormalizeCode(string? code, out string normalizedCode)
    {
        normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalizedCode.Length is > 0 and <= 50
            && normalizedCode.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }
}
