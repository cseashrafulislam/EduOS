using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.LMS;

public sealed class TeacherResourceDto
{
    public Guid Reference { get; set; }
    public long CampusId { get; set; }
    public long AuthorEmployeeId { get; set; }
    public long? AcademicLevelId { get; set; }
    public long? SubjectId { get; set; }
    public Guid? LessonPlanReference { get; set; }
    public Guid? SupersedesReference { get; set; }
    public string Title { get; set; } = string.Empty;
    public string KindCode { get; set; } = string.Empty;
    public string ScopeCode { get; set; } = string.Empty;
    public string StateCode { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string RightsHolder { get; set; } = string.Empty;
    public string LicenceCode { get; set; } = string.Empty;
    public string LicenceEvidence { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileBytes { get; set; }
    public int VersionNo { get; set; }
    public long? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public bool CanModify { get; set; }
}

public sealed class TeacherResourceUploadRequestDto
{
    [Required] public Guid RequestId { get; set; }
    public long CampusId { get; set; }
    public Guid? LessonPlanReference { get; set; }
    public long? AcademicLevelId { get; set; }
    public long? SubjectId { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(24)] public string KindCode { get; set; } = "Worksheet";
    [Required, MaxLength(24)] public string ScopeCode { get; set; } = "Private";
    [MaxLength(150)] public string? Topic { get; set; }
    [Required, MaxLength(10)] public string LanguageCode { get; set; } = "bn-BD";
    [MaxLength(1000)] public string? SourceUrl { get; set; }
    [Required, MaxLength(200)] public string RightsHolder { get; set; } = string.Empty;
    [Required, MaxLength(32)] public string LicenceCode { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string LicenceEvidence { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid? SupersedesReference { get; set; }
    public string? SupersedesRowVersion { get; set; }
}

public sealed class TeacherResourceTransitionRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class TeacherStudioOptionDto
{
    public long Id { get; set; }
    public Guid? Reference { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class TeacherStudioOptionsDto
{
    public IReadOnlyList<TeacherStudioOptionDto> Campuses { get; set; } = [];
    public IReadOnlyList<TeacherStudioOptionDto> Subjects { get; set; } = [];
    public IReadOnlyList<TeacherStudioOptionDto> Levels { get; set; } = [];
    public IReadOnlyList<TeacherStudioOptionDto> LessonPlans { get; set; } = [];
}
