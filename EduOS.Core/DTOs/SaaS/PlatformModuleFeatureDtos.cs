namespace EduOS.Core.DTOs.SaaS;

public sealed class PlatformModuleFeatureDirectoryDto
{
    public long ModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public bool IsCore { get; set; }
    public bool IsActive { get; set; }
    public string ModuleRowVersion { get; set; } = string.Empty;
    public List<PlatformModuleFeatureRowDto> Features { get; set; } = new();
}

public sealed class PlatformModuleFeatureRowDto
{
    public long FeatureId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsMapped { get; set; }
    public long? MappingId { get; set; }
    public string FeatureRowVersion { get; set; } = string.Empty;
    public string? MappingRowVersion { get; set; }
}

public sealed class SavePlatformModuleFeatureRequestDto
{
    public Guid RequestId { get; set; }
    [global::System.ComponentModel.DataAnnotations.Required]
    public string ModuleRowVersion { get; set; } = string.Empty;
    [global::System.ComponentModel.DataAnnotations.Required]
    public string FeatureRowVersion { get; set; } = string.Empty;
    public string? MappingRowVersion { get; set; }
    public bool IsMapped { get; set; }
}
