using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Students
{
    public class Guardian : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public int? UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Relation { get; set; } = string.Empty; // Father/Mother/Other
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? NID { get; set; }
        public string? Occupation { get; set; }
        public decimal? MonthlyIncome { get; set; }
        public string? Address { get; set; }
        public bool IsPrimary { get; set; } = false;

        public virtual Student? Student { get; set; }
        public virtual User? User { get; set; }
    }
}
