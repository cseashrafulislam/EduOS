using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    /// <summary>
    /// Represents a feature/module in the platform.
    /// Used for feature flags - which plans get which features.
    /// </summary>
    public class Feature : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? NameBangla { get; set; }
        public string Code { get; set; } = string.Empty; // e.g. "EXAM_MANAGEMENT"
        public string? Description { get; set; }
        public string? DescriptionBangla { get; set; }
        public string? Category { get; set; } // e.g. "Academic", "Finance", "HR"
        public string? IconName { get; set; }

        /// <summary>
        /// Display order in the features list
        /// </summary>
        public int DisplayOrder { get; set; }

        /// <summary>
        /// Is feature enabled at platform level (kill switch)
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Should this feature show in the marketing pricing comparison?
        /// </summary>
        public bool IsPubliclyVisible { get; set; } = true;

        // ==================== Navigation ====================

        public virtual ICollection<PlanFeature> PlanFeatures { get; set; } = new List<PlanFeature>();
        public virtual ICollection<ProductModuleFeature> ProductModules { get; set; } = new List<ProductModuleFeature>();
    }
}
