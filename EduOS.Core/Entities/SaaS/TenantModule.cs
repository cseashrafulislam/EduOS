using EduOS.Core.Entities.Base;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.SaaS;

/// <summary>
/// A tenant's explicit selection of a product module. Subscription entitlement is
/// evaluated separately so selecting a module cannot bypass the paid plan.
/// </summary>
public class TenantModule : BaseTenantEntity
{
    public long ProductModuleId { get; set; }
    public bool IsEnabled { get; set; }
    public TenantModuleActivationSource ActivationSource { get; set; } =
        TenantModuleActivationSource.InstitutionPreset;
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveUntilUtc { get; set; }
    public string? DisabledReason { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public int ConfigurationVersion { get; set; } = 1;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ProductModule? ProductModule { get; set; }
}
