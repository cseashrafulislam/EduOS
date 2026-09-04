using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    public class TrialAccount : BaseEntity
    {
        public int TenantId { get; set; }
        public DateTime TrialStartDate { get; set; }
        public DateTime TrialEndDate { get; set; }
        public int TrialDays { get; set; }
        public bool IsConverted { get; set; } = false;
        public DateTime? ConvertedDate { get; set; }
        public int? ConvertedToPlanId { get; set; }

        public virtual Tenant? Tenant { get; set; }
    }
}
