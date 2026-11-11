using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS;

public sealed class SmartSetupPreviewDto
{
    public long InstitutionTypeId { get; set; }
    public string InstitutionTypeName { get; set; } = string.Empty;
    public string InstitutionTypeCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string TemplateVersion { get; set; } = string.Empty;
    public List<SmartSetupModuleDto> Modules { get; set; } = new();
}

public sealed class SmartSetupModuleDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameBangla { get; set; } = string.Empty;
    public string NextStepUrl { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool SupportsSafeApply { get; set; }
    public string? Guidance { get; set; }
    public List<SmartSetupEntryDto> Entries { get; set; } = new();
}

public sealed class SmartSetupEntryDto
{
    public string Kind { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // Missing, Existing, Conflict or Disabled. No editable financial values.
    public string Status { get; set; } = string.Empty;
}

public sealed class ApplySmartSetupRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long InstitutionTypeId { get; set; }
    [Required] public string TemplateVersion { get; set; } = string.Empty;
    [Required] public List<string> Modules { get; set; } = new();
    public bool Confirmed { get; set; }
}

public sealed class SmartSetupApplyResultDto
{
    public int CreatedCount { get; set; }
    public int ExistingCount { get; set; }
    public string TemplateVersion { get; set; } = string.Empty;
    public List<SmartSetupEntryDto> Created { get; set; } = new();
}
