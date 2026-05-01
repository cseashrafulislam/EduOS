using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class Holiday : BaseTenantEntity
    {
        public int AcademicYearId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Type { get; set; } = "Public"; // Public/Religious/Vacation
        public string? Description { get; set; }

        public virtual AcademicYear? AcademicYear { get; set; }
    }
}
