namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentComponentDto
{
    public long Id { get; set; }
    public long AssessmentSubjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public decimal Weightage { get; set; }
    public bool IsMandatoryPass { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveAssessmentComponentRequestDto
{
    [Range(1, long.MaxValue)] public long AssessmentSubjectId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Range(typeof(decimal), "0", "100000")] public decimal FullMarks { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal PassMarks { get; set; }
    [Range(typeof(decimal), "0", "100")] public decimal Weightage { get; set; } = 100;
    public bool IsMandatoryPass { get; set; }
    public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class StudentAssessmentComponentMarkDto
{
    public long Id { get; set; }
    public long AssessmentComponentId { get; set; }
    public long StudentAssessmentMarkId { get; set; }
    public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    public long EnteredByUserId { get; set; }
    public DateTime EnteredAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveStudentAssessmentComponentMarkRequestDto
{
    [Range(1, long.MaxValue)] public long AssessmentComponentId { get; set; }
    [Range(1, long.MaxValue)] public long StudentAssessmentMarkId { get; set; }
    [Range(typeof(decimal), "0", "100000")] public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class TranscriptIssueDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public string TranscriptNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public long IssuedByUserId { get; set; }
    public long? FileAssetId { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class IssueTranscriptRequestDto
{
    public Guid ClientRequestId { get; set; }
    public Guid StudentReference { get; set; }
    public DateOnly IssueDate { get; set; }
}

public sealed class RevokeTranscriptRequestDto
{
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class WithdrawResultPublicationRequestDto
{
    [Required, MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
