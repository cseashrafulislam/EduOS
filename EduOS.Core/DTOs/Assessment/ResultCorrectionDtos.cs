using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class ResultCorrectionComponentInputDto
{
    [Range(1, long.MaxValue)] public long AssessmentComponentId { get; set; }
    [Range(typeof(decimal), "0", "100000000")] public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}

public sealed class ResultCorrectionSubjectInputDto
{
    [Range(1, long.MaxValue)] public long AssessmentSubjectId { get; set; }
    [Range(typeof(decimal), "0", "100000000")] public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<ResultCorrectionComponentInputDto> Components { get; set; } = [];
}

/// <summary>
/// This is a distinct controlled action after the independent re-scrutiny review;
/// it cannot authorize itself or replace an accepted case decision.
/// </summary>
public sealed class ApplyResultCorrectionRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required] public string PublicationRowVersion { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(1000)] public string Reason { get; set; } = string.Empty;
    public IReadOnlyList<ResultCorrectionSubjectInputDto> Subjects { get; set; } = [];
}

public sealed class ResultCorrectionDto
{
    public Guid CaseReference { get; set; }
    public long PreviousPublicationId { get; set; }
    public long NewPublicationId { get; set; }
    public int PreviousVersionNo { get; set; }
    public int NewVersionNo { get; set; }
    public long CorrectedByUserId { get; set; }
    public DateTime CorrectedAt { get; set; }
    public int CorrectedSubjectCount { get; set; }
    public string Reason { get; set; } = string.Empty;
}
