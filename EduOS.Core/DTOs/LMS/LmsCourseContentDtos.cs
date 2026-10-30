namespace EduOS.Core.DTOs.LMS;

public sealed class CourseContentDto
{
    public Guid CourseReference { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
    public bool IsArchived { get; set; }
    public IReadOnlyList<CourseSectionDto> Sections { get; set; } = [];
    public IReadOnlyList<ContentLessonDto> UnsectionedLessons { get; set; } = [];
}
public sealed class CourseSectionDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<ContentLessonDto> Lessons { get; set; } = [];
}
public sealed class ContentLessonDto
{
    public Guid Reference { get; set; }
    public string Title { get; set; } = string.Empty;
    public long? CourseSectionId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<LessonResourceDto> Resources { get; set; } = [];
}
public sealed class LessonResourceDto
{
    public long Id { get; set; }
    public Guid LessonReference { get; set; }
    public string? Title { get; set; }
    public string? ExternalUrl { get; set; }
    public Guid? FileAssetReference { get; set; }
    public bool FileDownloadable { get; set; }
    public string? FileName { get; set; }
    public int DisplayOrder { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class SaveCourseSectionRequestDto
{
    public Guid CourseReference { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Range(0, 10000)] public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class AssignLessonSectionRequestDto
{
    public Guid CourseReference { get; set; }
    public Guid LessonReference { get; set; }
    public long? CourseSectionId { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
}
public sealed class SaveLessonResourceRequestDto
{
    public Guid LessonReference { get; set; }
    [MaxLength(200)] public string? Title { get; set; }
    [MaxLength(1000)] public string? ExternalUrl { get; set; }
    public Guid? FileAssetReference { get; set; }
    [Range(0, 10000)] public int DisplayOrder { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class RemoveLessonResourceRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}