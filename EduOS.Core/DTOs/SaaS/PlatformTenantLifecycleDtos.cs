using System.ComponentModel.DataAnnotations;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.DTOs.SaaS;

public sealed class PlatformTenantLifecycleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public TenantState State { get; set; }
    public string StateName => State.ToString();
    public OnboardingStage OnboardingStage { get; set; }
    public bool EmailVerified { get; set; }
    public bool OnboardingComplete { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class ChangePlatformTenantStateRequestDto
{
    public TenantState TargetState { get; set; }
    [Required, StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
}
