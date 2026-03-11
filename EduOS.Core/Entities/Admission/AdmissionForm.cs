using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Admission
{
    public class AdmissionForm : TenantEntity
    {
        public int ApplicantId { get; set; }
        public string FormNo { get; set; }
        public DateTime ApplyDate { get; set; }
        public decimal FormFee { get; set; }
    }
}
