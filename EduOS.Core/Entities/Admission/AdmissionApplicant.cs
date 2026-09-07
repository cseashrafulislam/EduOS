using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;

namespace EduOS.Core.Entities.Admission;

/// <summary>
/// Institution-owned admission intake. Government identifiers deliberately do
/// not belong here; they are handled by the protected learner-identity workflow.
/// </summary>
public class AdmissionApplicant : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid ClientRequestId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public long CampusId { get; set; }
    public long AcademicUnitId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string? ApplicantNameBangla { get; set; }
    public DateTime DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string PrimaryMobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianRelation { get; set; }
    public string? GuardianMobile { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? PreviousInstitution { get; set; }
    public string PreferredLanguage { get; set; } = "bn-BD";
    public AdmissionApplicationStatus Status { get; set; } = AdmissionApplicationStatus.Submitted;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public string? DecisionNote { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual AcademicYear? AcademicYear { get; set; }
    public virtual AcademicTerm? AcademicTerm { get; set; }
    public virtual Campus? Campus { get; set; }
    public virtual Class? AcademicUnit { get; set; }
}
