using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class PlanFeature : BaseAuditableEntity
    {
        public int SubscriptionPlanId { get; set; }
        public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

        public int FeatureId { get; set; }
        public Feature Feature { get; set; } = null!;

        public bool IsEnabled { get; set; } = true;
    }
}