using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Admission
{
    public class AdmissionResult : BaseTenantEntity
    {
        public int AdmissionTestId { get; set; }
        public int ApplicantId { get; set; }
        public decimal ObtainedMarks { get; set; }
        public string ResultStatus { get; set; }
    }
}
