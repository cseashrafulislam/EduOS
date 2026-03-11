using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class TenantSubscription : TenantEntity
    {
        public int SubscriptionPlanId { get; set; }
        public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

        public decimal FixedAmount { get; set; }
        public decimal PerActiveStudentAmount { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public bool IsTrial { get; set; }
        public bool IsActive { get; set; } = true;
    }
}