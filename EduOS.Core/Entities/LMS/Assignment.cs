using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class Assignment : BaseTenantEntity
    {
        public int CourseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int TotalMark { get; set; }
        public DateTime DueDate { get; set; }
        public string? AttachmentUrl { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual Course? Course { get; set; }
    }
}
