using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS;

/// <summary>One teacher-owned goal per learner/course/chapter/skill; assessment grades remain canonical.</summary>
public sealed class AdaptiveStudyGoal : BaseTenantEntity
{
    public long CourseId { get; set; }
    public long StudentId { get; set; }
    public long CourseSectionId { get; set; }
    [Required, MaxLength(64)] public string SkillCode { get; set; } = string.Empty;
    [Column(TypeName = "decimal(5,2)")] public decimal TargetPercent { get; set; } = 80m;
    [MaxLength(500)] public string? TeacherNote { get; set; }
    public long SetByUserId { get; set; }
}

/// <summary>Immutable learner-requested historical snapshot, never a replacement for LMS QuizAttempt.</summary>
public sealed class AdaptiveStudyPlanHistory : BaseTenantEntity
{
    public Guid ClientRequestId { get; set; }
    public long CourseEnrollmentId { get; set; }
    public long StudentId { get; set; }
    public int AlgorithmVersion { get; set; } = 1;
    public DateTime GeneratedAt { get; set; }
    [Required] public string PlanJson { get; set; } = string.Empty;
}
