using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication
{
    public class Notice : BaseTenantEntity
    {
        public int? CategoryId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetAudience { get; set; } = "All"; // All/Student/Teacher/Parent
        public int? ClassId { get; set; }
        public string? AttachmentUrl { get; set; }
        public DateTime PublishDate { get; set; }
        public DateTime? ExpireDate { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual NoticeCategory? Category { get; set; }
        public virtual Class? Class { get; set; }
    }
}
