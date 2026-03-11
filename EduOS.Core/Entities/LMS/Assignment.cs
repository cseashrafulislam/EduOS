using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.LMS
{
    public class Assignment : TenantEntity
    {
        public int CourseId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime DueDate { get; set; }
    }
}
