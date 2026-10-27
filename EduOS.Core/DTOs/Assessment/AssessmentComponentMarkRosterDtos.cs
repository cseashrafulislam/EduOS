using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

public sealed class AssessmentComponentMarkRosterQueryDto : AssessmentScopeDto
{
    [Range(1, long.MaxValue)] public long AssessmentSubjectId { get; set; }
}

public sealed class AssessmentComponentMarkOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public decimal Weightage { get; set; }
    public bool IsMandatoryPass { get; set; }
}

public sealed class AssessmentComponentMarkEntryDto
{
    public long Id { get; set; }
    public long AssessmentComponentId { get; set; }
    public decimal ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssessmentComponentMarkStudentDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public long? StudentAssessmentMarkId { get; set; }
    public decimal? SubjectObtainedMarks { get; set; }
    public decimal? ExpectedSubjectMarks { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
    public bool MandatoryComponentsPassed { get; set; }
    public IReadOnlyList<AssessmentComponentMarkEntryDto> Marks { get; set; } = Array.Empty<AssessmentComponentMarkEntryDto>();
}

public sealed class AssessmentComponentMarkRosterDto
{
    public long AssessmentId { get; set; }
    public long AcademicBatchId { get; set; }
    public long AssessmentSubjectId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public bool IsResultPublished { get; set; }
    public bool IsEditable { get; set; }
    public IReadOnlyList<AssessmentComponentMarkOptionDto> Components { get; set; } = Array.Empty<AssessmentComponentMarkOptionDto>();
    public IReadOnlyList<AssessmentComponentMarkStudentDto> Students { get; set; } = Array.Empty<AssessmentComponentMarkStudentDto>();
}
