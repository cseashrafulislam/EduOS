namespace EduOS.Core.DTOs.Assessment;

/// <summary>
/// Authorized, read-only view of issued publication versions. This is not a
/// signed or independently version-pinned transcript document.
/// </summary>
public sealed class TranscriptAcademicRecordDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal? LatestPublishedCgpa { get; set; }
    public IReadOnlyList<TranscriptAcademicCourseDto> Courses { get; set; } =
        Array.Empty<TranscriptAcademicCourseDto>();
    public int LegacyPublishedResults { get; set; }
}

public sealed class TranscriptAcademicCourseDto
{
    public long PublicationId { get; set; }
    public int PublicationVersionNo { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public DateOnly AssessmentEndDate { get; set; }
    public string AcademicBatchName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public decimal CreditHours { get; set; }
    public decimal ObtainedMarks { get; set; }
    public decimal FullMarks { get; set; }
    public decimal GradePoint { get; set; }
    public string GradeLetter { get; set; } = string.Empty;
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
}
