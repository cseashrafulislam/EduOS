namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentAttemptRegistrationDto
{
    public long Id { get; set; }
    public long AssessmentSubjectId { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    public long PriorResultPublicationId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public AssessmentAttemptMode AttemptMode { get; set; }
    public AssessmentAttemptDecision Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? DecisionNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class RequestAssessmentAttemptDto
{
    public long AssessmentSubjectId { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    public long PriorResultPublicationId { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class DecideAssessmentAttemptDto
{
    public AssessmentAttemptDecision Decision { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Reason { get; set; } = string.Empty;
}

public sealed class AssessmentAttemptQueryDto
{
    public long AssessmentId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

public sealed class AssessmentAttemptCandidateQueryDto
{
    public long AssessmentSubjectId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

public sealed class AssessmentAttemptCandidateDto
{
    public long StudentSubjectRegistrationId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public long? PriorResultPublicationId { get; set; }
    public string PriorOutcome { get; set; } = "No eligible prior result";
    public bool CanRequest { get; set; }
}
