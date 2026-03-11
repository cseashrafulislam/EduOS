using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Admission
{
    public class AdmissionApplicant : TenantEntity
    {
        public string ApplicantName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public int? ApplyClassId { get; set; }
        public string Status { get; set; }
    }
}
