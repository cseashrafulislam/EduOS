using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    public class PlanFeature : BaseEntity
    {
        public int PlanId { get; set; }
        public int FeatureId { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int? LimitValue { get; set; } // null = unlimited

        public virtual SubscriptionPlan? Plan { get; set; }
        public virtual Feature? Feature { get; set; }
    }
}
