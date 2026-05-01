using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class Lesson : BaseTenantEntity
    {
        public int CourseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? VideoUrl { get; set; }
        public string? AttachmentUrl { get; set; }
        public int OrderNo { get; set; }
        public int Duration { get; set; } // minutes

        public virtual Course? Course { get; set; }
    }
}
