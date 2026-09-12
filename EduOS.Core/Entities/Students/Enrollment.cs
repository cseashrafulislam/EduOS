using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Entities.Students
{
    public class Enrollment : BaseTenantEntity
    {
        public long StudentId { get; set; }
        public long AcademicYearId { get; set; }
        public long ClassId { get; set; }
        public long SectionId { get; set; }
        public long? GroupId { get; set; }
        public long? CampusId { get; set; }
        public long? AcademicTermId { get; set; }
        public string Roll { get; set; } = string.Empty;
        public DateTime EnrollmentDate { get; set; }
        public bool IsActive { get; set; } = true;
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public virtual Student? Student { get; set; }
        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Section? Section { get; set; }
        public virtual Group? Group { get; set; }
        public virtual Campus? Campus { get; set; }
        public virtual AcademicTerm? AcademicTerm { get; set; }
    }
}
