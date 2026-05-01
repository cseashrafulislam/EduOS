using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class FeeReminder : BaseTenantEntity
    {
        public string ReminderType { get; set; } = "SMS"; // SMS/Email
        public int DaysBeforeDue { get; set; }
        public int DaysAfterDue { get; set; }
        public int? TemplateId { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual MessageTemplate? Template { get; set; }
    }
}
