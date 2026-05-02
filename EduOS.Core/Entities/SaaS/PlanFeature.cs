using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    /// <summary>
    /// Junction entity defining which features are included in which plan.
    /// Also supports per-plan feature limits (e.g. SMS quota differs by plan).
    /// </summary>
    public class PlanFeature : BaseEntity
    {
        public long SubscriptionPlanId { get; set; }
        public long FeatureId { get; set; }

        /// <summary>
        /// Is this feature enabled for this plan?
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Optional usage limit specific to this plan-feature combo
        /// (e.g. for SMS feature: 1000 in Basic, 5000 in Pro)
        /// </summary>
        public int? LimitValue { get; set; }

        /// <summary>
        /// Optional human-readable note (e.g. "Up to 5 reports/day")
        /// </summary>
        public string? Note { get; set; }

        // ==================== Navigation ====================

        public virtual SubscriptionPlan? SubscriptionPlan { get; set; }
        public virtual Feature? Feature { get; set; }
    }
}
