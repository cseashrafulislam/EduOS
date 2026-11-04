using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

// Stable report identifiers. Never expose arbitrary query/table names from client input.
public enum ExamReportKind
{
    Counselling = 1,
    AcademicTranscript = 2,
    Tabulation = 3,
    RollWiseResults = 4,
    Top20Positions = 5,
    RollWiseFail = 6,
    PassList = 7,
    GroupWiseSummary = 8,
    SubjectWisePosition = 9,
    SubjectWiseFail = 10,
    SubjectWisePass = 11,
    OneSubjectFail = 12,
    TwoSubjectFail = 13,
    ThreePlusSubjectFail = 14,
    PracticalSubjectFail = 15,
    GpaWiseResults = 16,
    StudentWiseSubject = 17,
    StudentWiseSubjectTotal = 18,
    SubjectMarkSheetBlank = 19
}

public sealed class ExamReportQueryDto
{
    [Range(1, long.MaxValue)] public long PublicationId { get; set; }
    public ExamReportKind Kind { get; set; }
    public long? AssessmentSubjectId { get; set; }
    [MaxLength(100)] public string? Search { get; set; }
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 25;
}

public sealed class ExamReportRowDto
{
    public Guid StudentReference { get; set; }
    public string RollNo { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? SubjectName { get; set; }
    public string? SubjectCode { get; set; }
    public string? GroupName { get; set; }
    public decimal? ObtainedMarks { get; set; }
    public decimal? TotalMarks { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? Gpa { get; set; }
    public decimal? Cgpa { get; set; }
    public string? GradeLetter { get; set; }
    public int? MeritPosition { get; set; }
    public int? SubjectPosition { get; set; }
    public int? FailedSubjects { get; set; }
    public int? StudentCount { get; set; }
    public int? PassedCount { get; set; }
    public int? FailedCount { get; set; }
    public int? WithheldCount { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
}

public sealed class ExamReportPageDto
{
    public long PublicationId { get; set; }
    public int VersionNo { get; set; }
    public ExamReportKind Kind { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public string AcademicBatchName { get; set; } = string.Empty;
    public bool IsWithdrawn { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<ExamReportRowDto> Items { get; set; } = Array.Empty<ExamReportRowDto>();
}

public sealed class ExamReportSubjectDto
{
    public long AssessmentSubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
}

public sealed class ExamReportScopeDto
{
    public long AssessmentId { get; set; }
    public long AcademicBatchId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public string AcademicBatchName { get; set; } = string.Empty;
}
