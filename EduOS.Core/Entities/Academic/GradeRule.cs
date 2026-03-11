using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class GradeRule : TenantEntity
    {
        public string GradeName { get; set; }
        public decimal MinMarks { get; set; }
        public decimal MaxMarks { get; set; }
        public decimal GradePoint { get; set; }
    }
}
