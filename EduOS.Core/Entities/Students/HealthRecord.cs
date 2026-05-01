using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class HealthRecord : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public string? BloodGroup { get; set; }
        public decimal? Height { get; set; } // cm
        public decimal? Weight { get; set; } // kg
        public string? Allergies { get; set; }
        public string? ChronicDiseases { get; set; }
        public string? Medications { get; set; }
        public string? EmergencyContact { get; set; }
        public DateTime? LastCheckupDate { get; set; }
        public string? Notes { get; set; }

        public virtual Student? Student { get; set; }
    }
}
