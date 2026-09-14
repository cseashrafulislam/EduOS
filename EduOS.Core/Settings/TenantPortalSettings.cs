namespace EduOS.Core.Settings;

/// <summary>
/// Public tenant portal addressing. Kept outside service code so every
/// environment can use its own verified base domain.
/// </summary>
public sealed class TenantPortalSettings
{
    public const string SectionName = "TenantPortal";

    public string BaseDomain { get; set; } = "eduos.com";
}
