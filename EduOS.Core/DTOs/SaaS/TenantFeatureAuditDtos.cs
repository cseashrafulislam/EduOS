namespace EduOS.Core.DTOs.SaaS;

public sealed class TenantFeatureAuditRowDto
{
    public long FeatureId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public bool IncludedInPlan { get; set; }
    public bool HasOverride { get; set; }
    public bool OverrideCurrentlyEffective { get; set; }
    public bool RestrictedInConfiguration { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Reason { get; set; }
}
