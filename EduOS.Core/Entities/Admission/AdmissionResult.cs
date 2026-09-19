using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Admission;

public class AdmissionResult : BaseTenantEntity
{
    public long AdmissionTestId { get; set; }
    public long ApplicantId { get; set; }
    public decimal ObtainedMarks { get; set; }
    public decimal Percentage { get; set; }
    public bool IsPassed { get; set; }
    public int? MeritPosition { get; set; }
    public string ResultStatus { get; set; } = string.Empty;
    public string? Grade { get; set; }
    public string? Remarks { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual AdmissionTest? AdmissionTest { get; set; }
    public virtual AdmissionApplicant? Applicant { get; set; }
}
