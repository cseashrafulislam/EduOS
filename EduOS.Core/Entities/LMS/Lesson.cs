using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.LMS
{
    public class Lesson : TenantEntity
    {
        public int CourseId { get; set; }
        public string Title { get; set; }
        public string VideoUrl { get; set; }
        public string AttachmentUrl { get; set; }
        public int SortOrder { get; set; }
    }
}
