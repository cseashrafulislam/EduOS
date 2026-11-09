namespace EduOS.Core.DTOs.LMS;

public sealed class AdaptiveCourseDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class AdaptiveChapterDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class AdaptiveMappedSkillDto
{
    public long CourseSectionId { get; set; }
    public string SkillCode { get; set; } = string.Empty;
    public string Chapter { get; set; } = string.Empty;
}

public sealed class AdaptiveRosterDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public sealed class AdaptiveGoalDto
{
    public long CourseSectionId { get; set; }
    public string SkillCode { get; set; } = string.Empty;
    public decimal TargetPercent { get; set; }
    public string? TeacherNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveAdaptiveGoalDto
{
    public Guid CourseReference { get; set; }
    public Guid StudentReference { get; set; }
    public long CourseSectionId { get; set; }
    [Required, MaxLength(64)] public string SkillCode { get; set; } = string.Empty;
    [Range(typeof(decimal), "0", "100")] public decimal TargetPercent { get; set; } = 80;
    [MaxLength(500)] public string? TeacherNote { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class AdaptiveSkillDto
{
    public long CourseSectionId { get; set; }
    public string Chapter { get; set; } = string.Empty;
    public string SkillCode { get; set; } = string.Empty;
    public decimal TargetPercent { get; set; }
    public decimal? ObservedPercent { get; set; }
    public string Priority { get; set; } = string.Empty;
    public int GradedAttempts { get; set; }
    public int PracticeAttemptsRemaining { get; set; }
    public Guid? RecommendedQuizReference { get; set; }
    public string? TeacherNote { get; set; }
}

public sealed class AdaptivePlanDto
{
    public Guid CourseReference { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public long? HistoryId { get; set; }
    public IReadOnlyList<AdaptiveSkillDto> Skills { get; set; } = Array.Empty<AdaptiveSkillDto>();
}

public sealed class SaveAdaptivePlanDto
{
    public Guid ClientRequestId { get; set; }
    public Guid CourseReference { get; set; }
}

public sealed class AdaptivePlanHistoryRowDto
{
    public long Id { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int SkillCount { get; set; }
}
