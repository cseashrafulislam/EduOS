namespace EduOS.Core.DTOs.SaaS;

public sealed class TenantFeatureRestrictionDto
{
    public long FeatureId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public bool IncludedInPlan { get; set; }
    public bool RestrictionConfigured { get; set; }
    public bool RestrictedNow { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Reason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveTenantFeatureRestrictionRequestDto
{
    public bool Restricted { get; set; }
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public string? Reason { get; set; }
    public string? RowVersion { get; set; }
}
