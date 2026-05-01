using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class Admission : BaseTenantEntity
    {
        public int AcademicYearId { get; set; }
        public int ClassId { get; set; }
        public string ApplicationNo { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string FatherName { get; set; } = string.Empty;
        public string MotherName { get; set; } = string.Empty;
        public DateTime DOB { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string? Religion { get; set; }
        public string? BloodGroup { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? PhotoUrl { get; set; }
        public DateTime ApplicationDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending/Approved/Rejected
        public decimal AdmissionFee { get; set; }
        public string? Remarks { get; set; }

        public virtual AcademicYear? AcademicYear { get; set; }
        public virtual Class? Class { get; set; }
    }
}
