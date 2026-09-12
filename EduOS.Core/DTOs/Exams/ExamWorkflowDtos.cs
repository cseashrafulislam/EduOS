using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Exams;

public class ExamScopeDto
{
    [Range(1, int.MaxValue)] public int ExamId { get; set; }
    [Range(1, int.MaxValue)] public int ClassId { get; set; }
    [Range(1, int.MaxValue)] public int SectionId { get; set; }
}

public sealed class ExamMarkRosterQueryDto : ExamScopeDto
{
    [Range(1, int.MaxValue)] public int SubjectId { get; set; }
}

public sealed class SaveExamMarkItemDto
{
    [Range(1, long.MaxValue)] public long StudentId { get; set; }
    [Range(typeof(decimal), "0", "1000000")] public decimal ObtainedMark { get; set; }
    public bool IsAbsent { get; set; }
}

public sealed class SaveExamMarksDto : ExamMarkRosterQueryDto
{
    [Required, MinLength(1)] public List<SaveExamMarkItemDto> Items { get; set; } = new();
}

public sealed class ExamMarkRosterItemDto
{
    public long StudentId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Roll { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal? ObtainedMark { get; set; }
    public bool IsAbsent { get; set; }
    public string? Grade { get; set; }
    public decimal? GPA { get; set; }
}

public sealed class ExamMarkRosterDto
{
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int FullMark { get; set; }
    public int PassMark { get; set; }
    public bool IsResultPublished { get; set; }
    public List<ExamMarkRosterItemDto> Students { get; set; } = new();
}

public sealed class ExamResultItemDto
{
    public long StudentId { get; set; }
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Roll { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal TotalMark { get; set; }
    public decimal TotalFullMark { get; set; }
    public decimal Percentage { get; set; }
    public decimal TotalGPA { get; set; }
    public string? FinalGrade { get; set; }
    public int? Position { get; set; }
    public bool IsPassed { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class ExamResultSheetDto
{
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public int AcademicYearId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public int SubjectCount { get; set; }
    public List<ExamResultItemDto> Results { get; set; } = new();
}
