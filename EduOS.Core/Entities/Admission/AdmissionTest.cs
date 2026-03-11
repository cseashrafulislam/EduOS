using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Admission
{
    public class AdmissionTest : TenantEntity
    {
        public string Name { get; set; }
        public int? ClassId { get; set; }
        public DateTime TestDate { get; set; }
        public decimal TotalMarks { get; set; }
    }
}
