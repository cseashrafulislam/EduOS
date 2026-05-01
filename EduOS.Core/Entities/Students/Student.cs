using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class Student : BaseTenantEntity
    {
        public int? UserId { get; set; }
        public int? AdmissionId { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string Roll { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FatherName { get; set; } = string.Empty;
        public string MotherName { get; set; } = string.Empty;
        public DateTime DOB { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string? BloodGroup { get; set; }
        public string? Religion { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? BirthCertNo { get; set; }
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public int? GroupId { get; set; }
        public int AcademicYearId { get; set; }
        public DateTime AdmissionDate { get; set; }
        public string? PhotoUrl { get; set; }
        public string Status { get; set; } = "Active"; // Active/TC/Passed/Dropout
        public bool IsActive { get; set; } = true;

        public virtual User? User { get; set; }
        public virtual Admission? Admission { get; set; }
        public virtual Class? Class { get; set; }
        public virtual Section? Section { get; set; }
        public virtual Group? Group { get; set; }
        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual ICollection<Guardian> Guardians { get; set; } = new List<Guardian>();
    }
}
