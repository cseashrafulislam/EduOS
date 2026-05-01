using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Admission
{
    public class AdmissionForm : BaseTenantEntity
    {
        public int ApplicantId { get; set; }
        public string FormNo { get; set; }
        public DateTime ApplyDate { get; set; }
        public decimal FormFee { get; set; }
    }
}
