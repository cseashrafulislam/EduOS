using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.LMS;

/// <summary>
/// A reviewed teacher-owned teaching asset. Bytes and scan results belong exclusively
/// to FileAsset; delivered LessonPlans and LMS LessonResources remain authoritative.
/// Each revision is a new immutable file/version, preserving previous moderation history.
/// </summary>
public sealed class TeacherResource : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long AuthorEmployeeId { get; set; }
    public long CampusId { get; set; }
    public long FileAssetId { get; set; }
    public long? LessonPlanId { get; set; }
    public long? AcademicLevelId { get; set; }
    public long? SubjectId { get; set; }
    public long? SupersedesResourceId { get; set; }
    public int VersionNo { get; set; } = 1;
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(24)] public string KindCode { get; set; } = "Worksheet";
    [Required, MaxLength(24)] public string ScopeCode { get; set; } = "Private";
    [Required, MaxLength(24)] public string StateCode { get; set; } = "Draft";
    [MaxLength(150)] public string? Topic { get; set; }
    [MaxLength(10)] public string LanguageCode { get; set; } = "bn-BD";
    [MaxLength(1000)] public string? SourceUrl { get; set; }
    [Required, MaxLength(200)] public string RightsHolder { get; set; } = string.Empty;
    [Required, MaxLength(32)] public string LicenceCode { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string LicenceEvidence { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    [MaxLength(500)] public string? ReviewNote { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
}
