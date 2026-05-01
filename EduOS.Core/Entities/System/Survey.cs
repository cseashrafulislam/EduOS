using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System
{
    public class Survey : BaseTenantEntity
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string TargetAudience { get; set; } = "All"; // Student/Teacher/Parent/All
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual ICollection<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();
    }
}
