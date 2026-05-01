using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Tenants;

namespace EduOS.Core.Entities.SaaS
{
    public class TenantSubscription : BaseEntity
    {
        public int TenantId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "Active"; // Active/Expired/Trial/Cancelled/Suspended
        public bool AutoRenew { get; set; } = true;
        public decimal Amount { get; set; }

        public virtual Tenant? Tenant { get; set; }
        public virtual SubscriptionPlan? Plan { get; set; }
    }
}
