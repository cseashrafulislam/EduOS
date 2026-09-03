using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS;

/// <summary>
/// A top-level EduOS product module. This is separate from granular billable
/// features so a module can contain multiple plan features.
/// </summary>
public class ProductModule : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameBangla { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public string? RoutePrefix { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsCore { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<InstitutionTypeModule> InstitutionTypes { get; set; } =
        new List<InstitutionTypeModule>();
}
