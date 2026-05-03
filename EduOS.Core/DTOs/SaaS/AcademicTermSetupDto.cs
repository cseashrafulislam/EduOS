namespace EduOS.Core.DTOs.SaaS
{
    public class AcademicTermSetupDto
    {
        public long? Id { get; set; }
        public long AcademicYearId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}