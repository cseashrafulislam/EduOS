using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Admission
{
    public class AdmissionResult : TenantEntity
    {
        public int AdmissionTestId { get; set; }
        public int ApplicantId { get; set; }
        public decimal ObtainedMarks { get; set; }
        public string ResultStatus { get; set; }
    }
}
