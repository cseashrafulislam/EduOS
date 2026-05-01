using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Exams
{
    public class GradeRule : BaseTenantEntity
    {
        public int MinMark { get; set; }
        public int MaxMark { get; set; }
        public string Grade { get; set; } = string.Empty;
        public decimal GPA { get; set; }
        public string? Remarks { get; set; }
    }
}
