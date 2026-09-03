using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS;

public class TenantModuleDto
{
    public long ProductModuleId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? IconName { get; set; }
    public string? RoutePrefix { get; set; }
    public bool IsCore { get; set; }
    public bool IsRequiredForInstitution { get; set; }
    public bool IsSelected { get; set; }
    public bool IsIncludedInPlan { get; set; }
    public bool IsAvailable { get; set; }
    public bool CanEnable { get; set; }
    public bool CanDisable { get; set; }
    public string ActivationSource { get; set; } = string.Empty;
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveUntilUtc { get; set; }
    public string AvailabilityReasonCode { get; set; } = string.Empty;
    public int ConfigurationVersion { get; set; }
    public string? RowVersion { get; set; }
}

public class UpdateTenantModuleRequestDto
{
    [Required]
    public bool? IsEnabled { get; set; }

    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveUntilUtc { get; set; }

    [MaxLength(500)]
    public string? DisabledReason { get; set; }

    [MaxLength(200)]
    public string? RowVersion { get; set; }
}
