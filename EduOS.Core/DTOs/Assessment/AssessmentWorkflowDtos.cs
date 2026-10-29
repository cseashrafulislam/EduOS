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

public sealed class AssessmentMarkRosterPageQueryDto : AssessmentScopeDto
{
    [Range(1, long.MaxValue)] public long AssessmentSubjectId { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 50;
    [MaxLength(100)] public string? Search { get; set; }
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
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
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
    public decimal? CumulativeGradePointAverage { get; set; }
    public decimal? CreditHours { get; set; }
    public IReadOnlyList<AssessmentResultCourseDto> Courses { get; set; } = Array.Empty<AssessmentResultCourseDto>();
    public string? GradeLetter { get; set; }
    public int? MeritPosition { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
}

public sealed class AssessmentResultCourseDto
{
    public long AssessmentSubjectId { get; set; }
    public long CurriculumSubjectId { get; set; }
    public long StudentSubjectRegistrationId { get; set; }
    public decimal FullMarks { get; set; }
    public decimal ObtainedMarks { get; set; }
    public decimal CreditHours { get; set; }
    public decimal GradePoint { get; set; }
    public string GradeLetter { get; set; } = string.Empty;
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
    public bool IsAbsent { get; set; }
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
    public Guid AssessmentReference { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long AcademicBatchId { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public long AssessmentSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public AssessmentState State { get; set; }
}

/// <summary>A bounded, campus-scoped page of publication evidence, not a global readiness certificate.</summary>
public sealed class AssessmentReadinessQueryDto : AssessmentScopeDto
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 20;
}
public sealed class AssessmentReadinessIssueDto
{
    public string SubjectName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
public sealed class AssessmentReadinessStudentDto
{
    public Guid StudentReference { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public int ApplicableSubjects { get; set; }
    public int RecordedMarks { get; set; }
    public int AbsentMarks { get; set; }
    public bool IsReady { get; set; }
    public IReadOnlyList<AssessmentReadinessIssueDto> Issues { get; set; } = Array.Empty<AssessmentReadinessIssueDto>();
}
public sealed class AssessmentReadinessPageDto
{
    public long AssessmentId { get; set; }
    public long AcademicBatchId { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<AssessmentReadinessStudentDto> Students { get; set; } = Array.Empty<AssessmentReadinessStudentDto>();
}
