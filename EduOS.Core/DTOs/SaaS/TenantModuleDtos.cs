using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.SaaS;

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
