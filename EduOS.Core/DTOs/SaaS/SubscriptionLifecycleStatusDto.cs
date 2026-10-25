namespace EduOS.Core.DTOs.SaaS;

/// <summary>Tenant-admin read model. AutoRenew is a preference, not a payment mandate.</summary>
public sealed class SubscriptionLifecycleStatusDto
{
    public Guid? SubscriptionReference { get; set; }
    public string State { get; set; } = "None";
    public DateTime ServerUtcNow { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public bool IsPeriodOverdue { get; set; }
    public bool AutoRenewEnabled { get; set; }
    public bool CanStartNewSubscription { get; set; }
    public bool AutomaticPaymentConfigured { get; set; }
    public string Guidance { get; set; } = string.Empty;
}
