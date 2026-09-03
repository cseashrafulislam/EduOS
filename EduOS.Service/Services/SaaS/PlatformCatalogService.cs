using System.Text.Json;
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
    private readonly IGenericRepository<ProductModule> _moduleRepository;
    private readonly ILogger<PlatformCatalogService> _logger;

    public PlatformCatalogService(
        IGenericRepository<InstitutionTypeDefinition> institutionTypeRepository,
        IGenericRepository<ProductModule> moduleRepository,
        ILogger<PlatformCatalogService> logger)
    {
        _institutionTypeRepository = institutionTypeRepository;
        _moduleRepository = moduleRepository;
        _logger = logger;
    }

    public async Task<ApiResponse<List<InstitutionTypeListItemDto>>> GetInstitutionTypesAsync()
    {
        try
        {
            var institutionTypes = await _institutionTypeRepository.GetQueryable()
                .AsNoTracking()
                .Where(x => x.IsActive && x.IsPubliclyVisible)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Name)
                .ToListAsync();
            var items = institutionTypes.Select(x => new InstitutionTypeListItemDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    NameBangla = x.NameBangla,
                    Description = x.Description,
                    AcademicCycleType = x.AcademicCycleType.ToString(),
                    DisplayOrder = x.DisplayOrder
                })
                .ToList();

            return ApiResponse<List<InstitutionTypeListItemDto>>.SuccessResponse(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load public institution types");
            return ApiResponse<List<InstitutionTypeListItemDto>>
                .ErrorResponse("Failed to load institution types", 500);
        }
    }

    public async Task<ApiResponse<InstitutionTypeDetailDto>> GetInstitutionTypeByCodeAsync(string code)
    {
        if (!TryNormalizeCode(code, out var normalizedCode))
        {
            return ApiResponse<InstitutionTypeDetailDto>
                .ErrorResponse("Institution type code is invalid", 400);
        }

        try
        {
            var institutionType = await _institutionTypeRepository.GetQueryable()
                .AsNoTracking()
                .Include(x => x.Modules)
                .ThenInclude(x => x.ProductModule)
                .FirstOrDefaultAsync(x =>
                    x.Code == normalizedCode && x.IsActive && x.IsPubliclyVisible);

            if (institutionType == null)
            {
                return ApiResponse<InstitutionTypeDetailDto>
                    .ErrorResponse("Institution type not found", 404);
            }

            var dto = new InstitutionTypeDetailDto
            {
                Id = institutionType.Id,
                Code = institutionType.Code,
                Name = institutionType.Name,
                NameBangla = institutionType.NameBangla,
                Description = institutionType.Description,
                AcademicCycleType = institutionType.AcademicCycleType.ToString(),
                DisplayOrder = institutionType.DisplayOrder,
                Terminology = DeserializeDictionary(institutionType.TerminologyJson, institutionType.Code, "terminology"),
                DefaultSettings = DeserializeDictionary(institutionType.DefaultSettingsJson, institutionType.Code, "settings"),
                Modules = institutionType.Modules
                    .Where(x => x.ProductModule is { IsActive: true })
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.ProductModule!.Name)
                    .Select(x => MapModule(x.ProductModule!, x))
                    .ToList()
            };

            return ApiResponse<InstitutionTypeDetailDto>.SuccessResponse(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load institution type {Code}", normalizedCode);
            return ApiResponse<InstitutionTypeDetailDto>
                .ErrorResponse("Failed to load institution type", 500);
        }
    }

    public async Task<ApiResponse<List<ProductModuleDto>>> GetModulesAsync()
    {
        try
        {
            var modules = await _moduleRepository.GetQueryable()
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Name)
                .Select(x => new ProductModuleDto
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    NameBangla = x.NameBangla,
                    Category = x.Category,
                    Description = x.Description,
                    IconName = x.IconName,
                    RoutePrefix = x.RoutePrefix,
                    DisplayOrder = x.DisplayOrder,
                    IsCore = x.IsCore
                })
                .ToListAsync();

            return ApiResponse<List<ProductModuleDto>>.SuccessResponse(modules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load product modules");
            return ApiResponse<List<ProductModuleDto>>
                .ErrorResponse("Failed to load modules", 500);
        }
    }

    private IReadOnlyDictionary<string, string> DeserializeDictionary(
        string json,
        string institutionTypeCode,
        string documentName)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Invalid {DocumentName} JSON for institution type {Code}",
                documentName,
                institutionTypeCode);
            return new Dictionary<string, string>();
        }
    }

    private static InstitutionTypeModuleDto MapModule(
        ProductModule module,
        InstitutionTypeModule mapping)
    {
        return new InstitutionTypeModuleDto
        {
            Id = module.Id,
            Code = module.Code,
            Name = module.Name,
            NameBangla = module.NameBangla,
            Category = module.Category,
            Description = module.Description,
            IconName = module.IconName,
            RoutePrefix = module.RoutePrefix,
            DisplayOrder = mapping.DisplayOrder,
            IsCore = module.IsCore,
            IsRequired = mapping.IsRequired,
            IsEnabledByDefault = mapping.IsEnabledByDefault
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
