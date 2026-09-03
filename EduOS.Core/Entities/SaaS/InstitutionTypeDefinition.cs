using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums.Academics;

namespace EduOS.Core.Entities.SaaS;

/// <summary>
/// A platform-managed, data-driven institution preset.
/// Tenants reference a preset instead of requiring institution-specific code branches.
/// </summary>
public class InstitutionTypeDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string? Description { get; set; }
    public AcademicCycleType AcademicCycleType { get; set; } = AcademicCycleType.Annual;
    public string TerminologyJson { get; set; } = "{}";
    public string DefaultSettingsJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPubliclyVisible { get; set; } = true;

    public virtual ICollection<InstitutionTypeModule> Modules { get; set; } =
        new List<InstitutionTypeModule>();

    public virtual ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
