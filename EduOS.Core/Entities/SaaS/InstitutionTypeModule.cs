using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS;

/// <summary>
/// Defines which modules are recommended or required for an institution preset.
/// Actual tenant activation and plan entitlement are enforced separately.
/// </summary>
public class InstitutionTypeModule : BaseEntity
{
    public long InstitutionTypeDefinitionId { get; set; }
    public long ProductModuleId { get; set; }
    public bool IsRequired { get; set; }
    public bool IsEnabledByDefault { get; set; } = true;
    public int DisplayOrder { get; set; }

    public virtual InstitutionTypeDefinition? InstitutionTypeDefinition { get; set; }
    public virtual ProductModule? ProductModule { get; set; }
}
