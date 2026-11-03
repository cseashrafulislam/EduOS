using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Research;

public sealed class AccreditationFrameworkCreateDto
{
    public Guid ClientRequestId { get; set; }
    [Required, StringLength(40, MinimumLength = 2)] public string Code { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 5)] public string Name { get; set; } = "";
    [Range(1, 1000)] public int VersionNo { get; set; }
}

public sealed class AccreditationStandardCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long FrameworkId { get; set; }
    [Required, StringLength(40, MinimumLength = 2)] public string Code { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 5)] public string Title { get; set; } = "";
    [Required, StringLength(2000, MinimumLength = 10)] public string Rubric { get; set; } = "";
    [Required, StringLength(2000, MinimumLength = 10)] public string RequiredEvidence { get; set; } = "";
    [Range(1, 3650)] public int ValidityDays { get; set; } = 365;
}

public sealed class AccreditationFrameworkPublishDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}

public sealed class AccreditationFrameworkDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int VersionNo { get; set; }
    public string StateCode { get; set; } = "";
    public string RowVersion { get; set; } = "";
}

public sealed class AccreditationStandardDto
{
    public long Id { get; set; }
    public long FrameworkId { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Rubric { get; set; } = "";
    public string RequiredEvidence { get; set; } = "";
    public int ValidityDays { get; set; }
}

public sealed class AccreditationAssessmentCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long? AcademicProgramId { get; set; }
    public long StandardId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    [Required, StringLength(3000, MinimumLength = 30)] public string EvidenceSummary { get; set; } = "";
}

public sealed class AccreditationAssessmentCommandDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string RowVersion { get; set; } = "";
    public long? ReviewerUserId { get; set; }
    [StringLength(3000)] public string? EvidenceSummary { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
    public bool? DeclaresNoConflict { get; set; }
}

public sealed class AccreditationAssessmentDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public long? AcademicProgramId { get; set; }
    public long StandardId { get; set; }
    public string StandardCode { get; set; } = "";
    public string FrameworkCode { get; set; } = "";
    public int FrameworkVersion { get; set; }
    public long OwnerUserId { get; set; }
    public long? ReviewerUserId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string? EvidenceSummary { get; set; }
    public string EvidenceSha256 { get; set; } = "";
    public int CurrentRevision { get; set; }
    public string StateCode { get; set; } = "";
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string RowVersion { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public List<string> AllowedActions { get; set; } = new();
}

public sealed class AccreditationAssessmentHistoryDto
{
    public string ActionCode { get; set; } = "";
    public string FromState { get; set; } = "";
    public string ToState { get; set; } = "";
    public int RevisionNo { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class AccreditationReportRowDto
{
    public long CampusId { get; set; }
    public long? AcademicProgramId { get; set; }
    public string FrameworkCode { get; set; } = "";
    public int FrameworkVersion { get; set; }
    public string StandardCode { get; set; } = "";
    public int Total { get; set; }
    public int Approved { get; set; }
    public int Pending { get; set; }
    public int Expired { get; set; }
}
