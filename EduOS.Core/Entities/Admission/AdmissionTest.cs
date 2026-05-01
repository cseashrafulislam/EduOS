using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Admission
{
    public class AdmissionTest : BaseTenantEntity
    {
        public string Name { get; set; }
        public int? ClassId { get; set; }
        public DateTime TestDate { get; set; }
        public decimal TotalMarks { get; set; }
    }
}
