using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    public class TrialAccount : BaseEntity
    {
        public long TenantId { get; set; }
        public DateTime TrialStartDate { get; set; }
        public DateTime TrialEndDate { get; set; }
        public int TrialDays { get; set; }
        public bool IsConverted { get; set; } = false;
        public DateTime? ConvertedDate { get; set; }
        public long? ConvertedToPlanId { get; set; }

        public virtual Tenant? Tenant { get; set; }
        public virtual SubscriptionPlan? ConvertedToPlan { get; set; }
    }
}
