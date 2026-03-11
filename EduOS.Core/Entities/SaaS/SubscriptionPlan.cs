using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.SaaS
{
    public class SubscriptionPlan : BaseAuditableEntity
    {
        public string Name { get; set; } = string.Empty;          // Starter / Pro / Enterprise
        public string Code { get; set; } = string.Empty;          // STARTER / PRO / ENTERPRISE
        public string BillingType { get; set; } = string.Empty;   // Fixed / PerStudent / Hybrid

        public decimal FixedAmount { get; set; }
        public decimal PerActiveStudentAmount { get; set; }

        public int? MinStudentLimit { get; set; }
        public int? MaxStudentLimit { get; set; }

        public bool IsTrialAvailable { get; set; }
        public int TrialDays { get; set; }

        public bool IsActive { get; set; } = true;
    }
}