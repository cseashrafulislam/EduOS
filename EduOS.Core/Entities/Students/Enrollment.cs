using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.SaaS;

namespace EduOS.Core.Entities.Students
{
    public class Enrollment : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int AcademicYearId { get; set; }
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public int? GroupId { get; set; }
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
