using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

/// <summary>Read/query scope. Academic level and track are resolved from the selected batch, not supplied redundantly.</summary>
public class AssessmentScopeDto
{
    [Range(1, long.MaxValue)] public long AssessmentId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
}

public sealed class AssessmentMarkRosterQueryDto : AssessmentScopeDto
{
    [Range(1, long.MaxValue)] public long AssessmentSubjectId { get; set; }
}

public sealed class AssessmentMarkRosterItemDto
{
    public Guid StudentReference { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal? ObtainedMarks { get; set; }
    public bool IsAbsent { get; set; }
    public bool IsWithheld { get; set; }
    public string? GradeLetter { get; set; }
    public decimal? GradePoint { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class AssessmentMarkRosterDto
{
    public long AssessmentId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public long AcademicBatchId { get; set; }
    public long AssessmentSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public decimal FullMarks { get; set; }
    public decimal PassMarks { get; set; }
    public bool IsResultPublished { get; set; }
    public IReadOnlyList<AssessmentMarkRosterItemDto> Students { get; set; } = Array.Empty<AssessmentMarkRosterItemDto>();
}

public sealed class AssessmentResultItemDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal TotalMarks { get; set; }
    public decimal MaximumMarks { get; set; }
    public decimal Percentage { get; set; }
    public decimal? GradePointAverage { get; set; }
    public string? GradeLetter { get; set; }
    public int? MeritPosition { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
}

public sealed class AssessmentResultSheetDto
{
    public long AssessmentId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long AcademicBatchId { get; set; }
    public int SubjectCount { get; set; }
    public DateTime? PublishedAt { get; set; }
    public IReadOnlyList<AssessmentResultItemDto> Results { get; set; } = Array.Empty<AssessmentResultItemDto>();
}

public sealed class AssessmentScopeOptionDto
{
    public long AssessmentId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public long AssessmentSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public AssessmentState State { get; set; }
}
