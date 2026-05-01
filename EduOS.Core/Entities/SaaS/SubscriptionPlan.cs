using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.SaaS
{
    public class SubscriptionPlan : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string BillingCycle { get; set; } = "Monthly"; // Monthly/Yearly
        public int MaxStudents { get; set; }
        public int MaxTeachers { get; set; }
        public int MaxStorageMB { get; set; }
        public int TrialDays { get; set; } = 0;
        public bool IsActive { get; set; } = true;

        public virtual ICollection<PlanFeature> PlanFeatures { get; set; } = new List<PlanFeature>();
    }
}
