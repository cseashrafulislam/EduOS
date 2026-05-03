namespace EduOS.Core.DTOs.SaaS
{
    public class AcademicYearSetupDto
    {
        public long? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsCurrent { get; set; }
    }
}