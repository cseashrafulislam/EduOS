using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.LMS
{
    public class Homework : BaseTenantEntity
    {
        public long ClassId { get; set; }
        public long SectionId { get; set; }
        public long SubjectId { get; set; }
        public long TeacherId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime AssignedDate { get; set; }
        public DateTime DueDate { get; set; }
        public string? AttachmentUrl { get; set; }

        public virtual Class? Class { get; set; }
        public virtual Section? Section { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual Employee? Teacher { get; set; }
    }
}
