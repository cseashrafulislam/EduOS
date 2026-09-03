using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS;

/// <summary>
/// Connects a top-level product module to its granular subscription features.
/// </summary>
public class ProductModuleFeature : BaseEntity
{
    public long ProductModuleId { get; set; }
    public long FeatureId { get; set; }
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }

    public virtual ProductModule? ProductModule { get; set; }
    public virtual Feature? Feature { get; set; }
}
