using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Auth;

public sealed class SwitchInstitutionRequestDto
{
    [Range(1, long.MaxValue)]
    public long TenantId { get; set; }
}
