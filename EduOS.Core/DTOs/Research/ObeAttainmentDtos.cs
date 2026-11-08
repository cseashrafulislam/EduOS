using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EduOS.Core.DTOs.Research;

public sealed class ObePolicyCreateDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public long AcademicProgramId { get; set; }
    [Range(1, 1000)] public int VersionNo { get; set; }
    public List<ObeCourseOutcomeInputDto> CourseOutcomes { get; set; } = new();
    public List<ObePloContributionInputDto> Contributions { get; set; } = new();
}

public sealed class ObeCourseOutcomeInputDto
{
    public long CurriculumSubjectId { get; set; }
    // Omit null in the policy request digest so pre-upgrade course-proxy
    // idempotency keys remain replay-compatible after this additive extension.
    [StringLength(50), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ComponentCode { get; set; }
    [Required, StringLength(40, MinimumLength = 2)] public string Code { get; set; } = "";
    [Required, StringLength(1000, MinimumLength = 10)] public string Statement { get; set; } = "";
}

public sealed class ObePloContributionInputDto
{
    [Required] public string CourseOutcomeCode { get; set; } = "";
    public long ProgramOutcomeId { get; set; }
    [Range(typeof(decimal), "0.0001", "100")] public decimal WeightPercent { get; set; }
}

public sealed class ObePolicyDto
{
    public long Id { get; set; }
    public long CampusId { get; set; }
    public long AcademicProgramId { get; set; }
    public int VersionNo { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ObeCourseOutcomeDto
{
    public long Id { get; set; }
    public long CurriculumSubjectId { get; set; }
    public string? ComponentCode { get; set; }
    public string Code { get; set; } = "";
    public string Statement { get; set; } = "";
}

public sealed class ObePloContributionDto
{
    public long CourseOutcomeId { get; set; }
    public long ProgramOutcomeId { get; set; }
    public string ProgramOutcomeCode { get; set; } = "";
    public decimal WeightPercent { get; set; }
}

public sealed class ObePolicyDetailDto
{
    public ObePolicyDto Policy { get; set; } = new();
    public List<ObeCourseOutcomeDto> CourseOutcomes { get; set; } = new();
    public List<ObePloContributionDto> Contributions { get; set; } = new();
}

public sealed class ObeCurriculumCourseDto
{
    public long CurriculumSubjectId { get; set; }
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
}

public sealed class ObePublishedBatchDto
{
    public long ResultPublicationId { get; set; }
    public long AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = "";
    public long? AcademicTermId { get; set; }
    public string? AcademicTermName { get; set; }
    public int VersionNo { get; set; }
    public string AssessmentName { get; set; } = "";
    public string BatchName { get; set; } = "";
    public DateTime PublishedAtUtc { get; set; }
}

public sealed class ObePublishedComponentCatalogDto
{
    public bool HasFrozenRegister { get; set; }
    public List<ObePublishedComponentOptionDto> Components { get; set; } = new();
}

public sealed class ObePublishedComponentOptionDto
{
    public long CurriculumSubjectId { get; set; }
    public string ComponentCode { get; set; } = "";
    public int RecordedRowCount { get; set; }
    public int LearnerCount { get; set; }
    public bool CompleteForCourse { get; set; }
}

public sealed class ObeOutcomeAttainmentDto
{
    public long ProgramOutcomeId { get; set; }
    public string ProgramOutcomeCode { get; set; } = "";
    public decimal AttainmentPercent { get; set; }
    public int EvidenceCount { get; set; }
}

public sealed class ObeProgrammeRollupDto
{
    public long PolicyId { get; set; }
    public long AcademicYearId { get; set; }
    public long? AcademicTermId { get; set; }
    public int ApprovedPublicationCount { get; set; }
    // The same learner can appear in more than one assessment. This is not a
    // unique-person count and must not be presented as one.
    public long LearnerPublicationObservations { get; set; }
    public List<Guid> SourceReportReferences { get; set; } = new();
    public List<ObeOutcomeAttainmentDto> Outcomes { get; set; } = new();
}

public sealed class ObeCloAttainmentDto
{
    public long CourseOutcomeId { get; set; }
    public string CourseOutcomeCode { get; set; } = "";
    public long CurriculumSubjectId { get; set; }
    public string? ComponentCode { get; set; }
    public decimal AttainmentPercent { get; set; }
}

public sealed class ObePreviewDto
{
    public long PolicyId { get; set; }
    public long ResultPublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public int LearnerCount { get; set; }
    public string EvidenceSha256 { get; set; } = "";
    public List<ObeCloAttainmentDto> CourseOutcomes { get; set; } = new();
    public List<ObeOutcomeAttainmentDto> Outcomes { get; set; } = new();
}

public sealed class ObeSubmitDto
{
    public Guid ClientRequestId { get; set; }
    public long ResultPublicationId { get; set; }
}

public sealed class ObeReviewDto
{
    public Guid ClientRequestId { get; set; }
    [Required, RegularExpression("^(Approve|Reject)$")] public string Decision { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 10)] public string Reason { get; set; } = "";
}

public sealed class ObeReportDto
{
    public Guid Reference { get; set; }
    public long PolicyId { get; set; }
    public long CampusId { get; set; }
    public long AcademicProgramId { get; set; }
    public long ResultPublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public string StateCode { get; set; } = "";
    public long SubmittedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public ObePreviewDto Snapshot { get; set; } = new();
}
