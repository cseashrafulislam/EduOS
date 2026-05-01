using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Employees;

namespace EduOS.Core.Entities.Academic
{
    public class Substitution : BaseTenantEntity
    {
        public DateTime Date { get; set; }
        public int OriginalTeacherId { get; set; }
        public int SubstituteTeacherId { get; set; }
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public string? Period { get; set; }
        public string? Reason { get; set; }

        public virtual Employee? OriginalTeacher { get; set; }
        public virtual Employee? SubstituteTeacher { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Subject? Subject { get; set; }
    }
}
