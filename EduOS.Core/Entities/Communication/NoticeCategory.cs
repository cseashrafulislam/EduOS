using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class NoticeCategory : BaseTenantEntity
    {
        public string Name { get; set; } = string.Empty; // Urgent/General/Academic/Examination/Event
        public string? Color { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
