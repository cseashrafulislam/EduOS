namespace EduOS.Core.DTOs.SaaS
{
    public class AcademicTermSetupDto
    {
        public long? Id { get; set; }
        public long AcademicYearId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? TermType { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsCurrent { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; } = 1;
    }
}