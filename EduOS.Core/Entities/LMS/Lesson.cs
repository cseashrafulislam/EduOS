using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS
{
    public class Lesson : BaseTenantEntity
    {
        public Guid PublicId { get; set; } = Guid.NewGuid();
        public long CourseId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? VideoUrl { get; set; }
        public string? AttachmentUrl { get; set; }
        public int OrderNo { get; set; }
        public int Duration { get; set; }
        public virtual Course? Course { get; set; }
        public virtual ICollection<LessonProgress> Progress { get; set; } = new List<LessonProgress>();
    }
}
