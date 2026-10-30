using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS;

/// <summary>Append-only, tenant-bound lesson authoring evidence. Never serves historical private files.</summary>
public sealed class LmsLessonRevision : BaseTenantEntity
{
    public long LessonId { get; set; }
    public long CourseId { get; set; }
    public int RevisionNo { get; set; }
    [Required, MaxLength(32)] public string ChangeKind { get; set; } = "Lesson";
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Content { get; set; }
    [MaxLength(500)] public string? ContentUrl { get; set; }
    public long? CourseSectionId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    [Required] public string ResourceSnapshotJson { get; set; } = "[]";
    public DateTime CapturedAt { get; set; }
    public long CapturedByUserId { get; set; }
}
