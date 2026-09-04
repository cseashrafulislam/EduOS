using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class InstructorAssignment : BaseTenantEntity
    {
        public long AcademicBatchId { get; set; }
        public long SubjectId { get; set; }
        public long EmployeeId { get; set; }

        public long AcademicYearId { get; set; }
        public long? AcademicTermId { get; set; }

        public bool IsPrimary { get; set; } = true;
        public bool IsClassAdvisor { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual AcademicBatch? AcademicBatch { get; set; }
        public virtual Subject? Subject { get; set; }
        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual AcademicTerm? AcademicTerm { get; set; }
    }
}