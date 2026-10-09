namespace EduOS.Core.DTOs.SaaS;

public sealed class CancelSubscriptionRequestDto
{
    public bool AtPeriodEnd { get; set; } = true;
    [MaxLength(1000)] public string? Reason { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class UpdateSubscriptionAutoRenewRequestDto
{
    public bool AutoRenew { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
