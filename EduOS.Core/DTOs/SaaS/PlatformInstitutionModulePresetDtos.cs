namespace EduOS.Core.DTOs.SaaS;

public sealed class PlatformInstitutionModulePresetDto
{
    public long InstitutionTypeId { get; set; }
    public string InstitutionTypeCode { get; set; } = string.Empty;
    public string InstitutionTypeName { get; set; } = string.Empty;
    public string InstitutionTypeRowVersion { get; set; } = string.Empty;
    public List<PlatformInstitutionModuleRowDto> Modules { get; set; } = new();
}

public sealed class PlatformInstitutionModuleRowDto
{
    public long ModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public bool IsCore { get; set; }
    public bool IsActive { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDefaultEnabled { get; set; }
    public long? PresetId { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class SavePlatformInstitutionModulePresetDto
{
    public Guid RequestId { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string InstitutionTypeRowVersion { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDefaultEnabled { get; set; }
}
